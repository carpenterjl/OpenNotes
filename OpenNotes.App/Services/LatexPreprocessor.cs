using System.Text;
using System.Text.RegularExpressions;

namespace OpenNotes.Services;

/// <summary>Normalizes user-authored LaTeX into the dialect WpfMath 2.1 can actually parse.
/// WpfMath supports a subset of TeX (empirically probed): no <c>%</c> comments, no <c>$</c>
/// delimiters, no <c>\mathbf</c>/<c>\textbf</c>/amsmath environments — but it does support
/// bare <c>\\</c> line stacking, <c>\begin{align}</c>/<c>\begin{pmatrix}</c>, and the
/// <c>\matrix{…}</c>/<c>\cases{…}</c> commands. This class strips what must be ignored
/// (comments, math delimiters), rewrites what has a supported equivalent (font commands,
/// amsmath environments, <c>\dfrac</c>, <c>\implies</c>, …), and turns hard newlines into
/// <c>\\</c> so multi-line input renders as stacked lines. Approximations are deliberate:
/// bold has no WpfMath equivalent, so <c>\mathbf</c> falls back to upright <c>\mathrm</c>.</summary>
public static partial class LatexPreprocessor
{
    /// <summary>Command alias table: unsupported command → supported replacement.
    /// Order-independent; all are plain token substitutions (arguments stay in place).</summary>
    private static readonly (string From, string To)[] CommandAliases =
    {
        // Font/style commands (WpfMath has no bold — \mathrm is the closest upright form).
        (@"\mathbf", @"\mathrm"),
        (@"\boldsymbol", @"\mathrm"),
        (@"\bm", @"\mathrm"),
        (@"\mathbb", @"\mathcal"),
        (@"\Bbb", @"\mathcal"),
        (@"\mathfrak", @"\mathcal"),
        (@"\mathsf", @"\mathrm"),
        (@"\mathtt", @"\mathrm"),
        (@"\operatorname", @"\mathrm"),
        (@"\mathop", @"\mathrm"),
        (@"\textbf", @"\text"),
        (@"\textit", @"\text"),
        (@"\textrm", @"\text"),
        (@"\textsf", @"\text"),
        (@"\texttt", @"\text"),
        (@"\mbox", @"\text"),
        (@"\hbox", @"\text"),
        (@"\fbox", @"\text"),
        // Fractions and dots.
        (@"\dfrac", @"\frac"),
        (@"\tfrac", @"\frac"),
        (@"\cfrac", @"\frac"),
        (@"\dotsb", @"\cdots"),
        (@"\dotsc", @"\ldots"),
        (@"\dotsi", @"\cdots"),
        (@"\dotso", @"\ldots"),
        (@"\dots", @"\ldots"),
        (@"\ddots", @"\cdots"),
        (@"\vdots", @"\ldots"),
        // Logic / arrows / symbols.
        (@"\implies", @"\;\Rightarrow\;"),
        (@"\impliedby", @"\;\Leftarrow\;"),
        (@"\iff", @"\;\Leftrightarrow\;"),
        (@"\land", @"\wedge"),
        (@"\lor", @"\vee"),
        (@"\lnot", @"\neg"),
        (@"\varnothing", @"\emptyset"),
        (@"\lVert", @"\|"),
        (@"\rVert", @"\|"),
        (@"\lvert", "|"),
        (@"\rvert", "|"),
        // Decorations with a close supported cousin.
        (@"\overbrace", @"\overline"),
        (@"\underbrace", @"\underline"),
        (@"\overset", @"\stackrelalias"),   // handled structurally below
        // Spacing.
        (@"\qquad", @"\;\;\;\;"),
        (@"\quad", @"\;\;"),
        (@"\thinspace", @"\,"),
        (@"\enspace", @"\;"),
        // mod
        (@"\bmod", @"\;\mathrm{mod}\;"),
    };

    /// <summary>Commands that WpfMath does not know and that can simply be dropped
    /// (their braces/arguments remain and still parse).</summary>
    private static readonly string[] StrippedCommands =
    {
        @"\displaystyle", @"\textstyle", @"\scriptstyle", @"\scriptscriptstyle",
        @"\limits", @"\nolimits", @"\substack", @"\boxed", @"\ensuremath",
        @"\bf", @"\rm", @"\it", @"\sf", @"\tt", @"\cal", @"\em",
    };

    /// <summary>Normalize raw user LaTeX to WpfMath-parseable form. Never throws;
    /// returns an empty string for null/blank input.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = StripComments(raw);
        s = StripMathDelimiters(s);
        s = RewriteEnvironments(s);
        s = ApplyAliases(s);
        s = RemoveNullDelimiters(s);
        s = RewriteTwoArgStacks(s);
        s = RewriteSpacingWithArgs(s);
        s = NewlinesToLineBreaks(s);
        s = HandleBareAmpersands(s);
        return s.Trim();
    }

    /// <summary>Remove unescaped <c>%</c> comments (to end of line). <c>\%</c> is a literal
    /// percent and survives. Char-scan instead of regex so backslash-escape runs are exact.</summary>
    private static string StripComments(string input)
    {
        var sb = new StringBuilder(input.Length);
        bool escaped = false, inComment = false;
        foreach (var c in input)
        {
            if (inComment)
            {
                if (c == '\n') { inComment = false; sb.Append(c); }
                continue;
            }
            if (escaped) { sb.Append(c); escaped = false; continue; }
            if (c == '\\') { sb.Append(c); escaped = true; continue; }
            if (c == '%') { inComment = true; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Drop <c>$</c>/<c>$$</c>/<c>\[…\]</c>/<c>\(…\)</c> math-mode delimiters — the whole
    /// block is already math. <c>\\[6pt]</c> line-break spacing collapses to a plain <c>\\</c> first
    /// so its bracket is not mistaken for a display-math delimiter. <c>\$</c> stays literal.</summary>
    private static string StripMathDelimiters(string input)
    {
        var s = Regex.Replace(input, @"\\\\\[[0-9.]+\s*(pt|em|ex|cm|mm|in)?\]", @"\\");
        s = Regex.Replace(s, @"(?<!\\)\$", string.Empty);
        s = s.Replace(@"\[", " ").Replace(@"\]", " ")
             .Replace(@"\(", " ").Replace(@"\)", " ");
        return s;
    }

    /// <summary>Map amsmath environments onto what WpfMath supports:
    /// align-family/gather/multline → <c>align</c>; equation wrappers are stripped;
    /// matrix variants → <c>\matrix{…}</c> wrapped in the right <c>\left…\right</c> fences;
    /// cases → <c>\cases{…}</c>; array (with column spec) → <c>\matrix{…}</c>.</summary>
    private static string RewriteEnvironments(string input)
    {
        var s = input;

        // align-family: normalize the environment name to plain "align".
        s = Regex.Replace(s, @"\\begin\{(align\*?|aligned|alignat\*?|flalign\*?|eqnarray\*?|split|gather\*?|gathered|multline\*?)\}",
            @"\begin{align}");
        s = Regex.Replace(s, @"\\end\{(align\*?|aligned|alignat\*?|flalign\*?|eqnarray\*?|split|gather\*?|gathered|multline\*?)\}",
            @"\end{align}");

        // equation is just a wrapper around a single formula.
        s = Regex.Replace(s, @"\\(begin|end)\{equation\*?\}", " ");

        // matrix variants → \matrix{…} with fences. Innermost-first so nesting unwinds.
        s = ReplaceEnvironment(s, "smallmatrix", body => @"\matrix{" + body + "}");
        s = ReplaceEnvironment(s, "matrix", body => @"\matrix{" + body + "}");
        s = ReplaceEnvironment(s, "bmatrix", body => @"\left[\matrix{" + body + @"}\right]");
        s = ReplaceEnvironment(s, "Bmatrix", body => @"\left\{\matrix{" + body + @"}\right\}");
        s = ReplaceEnvironment(s, "vmatrix", body => @"\left|\matrix{" + body + @"}\right|");
        s = ReplaceEnvironment(s, "Vmatrix", body => @"\left\|\matrix{" + body + @"}\right\|");
        s = ReplaceEnvironment(s, "cases", body => @"\cases{" + body + "}");
        s = ReplaceEnvironment(s, "array", body =>
        {
            // Drop the leading column spec ({c|c}, {ll}, …) if present.
            var m = Regex.Match(body, @"^\s*\{[lcr|@\s]*\}");
            if (m.Success) body = body[m.Length..];
            return @"\matrix{" + body + "}";
        });

        return s;
    }

    /// <summary>Replace innermost <c>\begin{env}…\end{env}</c> pairs (repeatedly, so sequential and
    /// nested occurrences all unwind). Case-sensitive, exact env name.</summary>
    private static string ReplaceEnvironment(string input, string env, Func<string, string> rewrite)
    {
        var pattern = @"\\begin\{" + Regex.Escape(env) + @"\}(?<body>(?:(?!\\begin\{" + Regex.Escape(env) + @"\}|\\end\{" + Regex.Escape(env) + @"\}).)*)\\end\{" + Regex.Escape(env) + @"\}";
        string previous;
        var s = input;
        do
        {
            previous = s;
            s = Regex.Replace(s, pattern, m => rewrite(m.Groups["body"].Value), RegexOptions.Singleline);
        } while (s != previous);
        return s;
    }

    private static string ApplyAliases(string input)
    {
        var s = input;
        foreach (var (from, to) in CommandAliases)
            s = ReplaceCommand(s, from, to);
        foreach (var cmd in StrippedCommands)
        {
            // \left. and \right. are literal fragments, not word-bounded commands.
            s = cmd is @"\left." or @"\right."
                ? s.Replace(cmd, " ")
                : ReplaceCommand(s, cmd, " ");
        }
        return s;
    }

    /// <summary>Token-exact command replacement: <c>\bf</c> must not eat the front of <c>\bfseries</c>
    /// or <c>\bford</c>. A command token ends where the next non-letter begins.</summary>
    private static string ReplaceCommand(string input, string command, string replacement)
        => Regex.Replace(input, Regex.Escape(command) + @"(?![a-zA-Z])", replacement.Replace("$", "$$"));

    /// <summary>WpfMath rejects the <c>.</c> null delimiter and unbalanced <c>\left</c>/<c>\right</c>.
    /// When either side of a matched pair is <c>.</c>, drop that side's token entirely and downgrade
    /// the partner to its bare delimiter (e.g. <c>\left. f \right|</c> → <c>f |</c>), keeping the
    /// pair balanced from the parser's point of view.</summary>
    private static string RemoveNullDelimiters(string input)
    {
        if (!input.Contains(@"\left") && !input.Contains(@"\right")) return input;

        // (start, length, delimiter, isLeft); delimiter is the token following \left/\right.
        var tokens = new List<(int Start, int Length, string Delim, bool IsLeft)>();
        foreach (Match m in Regex.Matches(input, @"\\(left|right)(\\[a-zA-Z]+|\\.|[^\s])"))
            tokens.Add((m.Index, m.Length, m.Groups[2].Value, m.Groups[1].Value == "left"));

        // Pair them with a stack; collect replacements for pairs involving a "." side.
        var replacements = new List<(int Start, int Length, string Text)>();
        var stack = new Stack<(int Start, int Length, string Delim)>();
        foreach (var t in tokens)
        {
            if (t.IsLeft) { stack.Push((t.Start, t.Length, t.Delim)); continue; }
            if (stack.Count == 0) continue; // unbalanced input; leave for the parser to report
            var left = stack.Pop();
            if (left.Delim != "." && t.Delim != ".") continue;

            replacements.Add((left.Start, left.Length, left.Delim == "." ? " " : left.Delim + " "));
            replacements.Add((t.Start, t.Length, t.Delim == "." ? " " : t.Delim + " "));
        }

        foreach (var (start, length, text) in replacements.OrderByDescending(r => r.Start))
            input = input[..start] + text + input[(start + length)..];
        return input;
    }

    /// <summary>Structural rewrites needing two brace arguments:
    /// <c>\stackrel{top}{base}</c>/<c>\overset{top}{base}</c> → <c>{base}^{top}</c>,
    /// <c>\underset{bottom}{base}</c> → <c>{base}_{bottom}</c>, <c>\pmod{n}</c> → <c>(mod n)</c>,
    /// plus the TeX primitives <c>{a \over b}</c> → <c>\frac</c>, <c>{n \choose k}</c> → <c>\binom</c>,
    /// <c>{a \atop b}</c> → stacked lines. Arguments must be brace-flat (the common case).</summary>
    private static string RewriteTwoArgStacks(string input)
    {
        const string arg = @"\{((?:[^{}]|\{[^{}]*\})*)\}"; // one brace group, one nesting level deep
        var s = input;
        s = Regex.Replace(s, @"\\stackrelalias\s*" + arg + @"\s*" + arg, "{$2}^{$1}");
        s = Regex.Replace(s, @"\\stackrel\s*" + arg + @"\s*" + arg, "{$2}^{$1}");
        s = Regex.Replace(s, @"\\underset\s*" + arg + @"\s*" + arg, "{$2}_{$1}");
        s = Regex.Replace(s, @"\\pmod\s*" + arg, @"\;(\mathrm{mod}\;$1)");
        s = Regex.Replace(s, @"\{([^{}]*)\\over(?![a-zA-Z])([^{}]*)\}", @"\frac{$1}{$2}");
        s = Regex.Replace(s, @"\{([^{}]*)\\choose(?![a-zA-Z])([^{}]*)\}", @"\binom{$1}{$2}");
        s = Regex.Replace(s, @"\{([^{}]*)\\atop(?![a-zA-Z])([^{}]*)\}", @"{$1 \\ $2}");
        return s;
    }

    /// <summary>Spacing/phantom commands whose argument is discarded.</summary>
    private static string RewriteSpacingWithArgs(string input)
    {
        var s = Regex.Replace(input, @"\\hspace\*?\s*\{[^{}]*\}", @"\;");
        s = Regex.Replace(s, @"\\(phantom|hphantom|vphantom)\s*\{[^{}]*\}", @"\;");
        return s;
    }

    /// <summary>Hard newlines become <c>\\</c> (WpfMath stacks bare <c>\\</c> vertically).
    /// Lines already ending in <c>\\</c> don't get a second one; blank lines collapse.</summary>
    private static string NewlinesToLineBreaks(string input)
    {
        var lines = input.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count <= 1) return lines.Count == 1 ? lines[0] : string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Count - 1 && !lines[i].EndsWith(@"\\", StringComparison.Ordinal))
                sb.Append(@" \\ ");
            else if (i < lines.Count - 1)
                sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>Bare <c>&amp;</c> alignment marks are only legal inside align/matrix constructs.
    /// If any remain outside one, wrap the whole formula in an align environment (multi-line
    /// input) or drop them (single line, e.g. copied from an equation array).</summary>
    private static string HandleBareAmpersands(string input)
    {
        if (!ContainsBareAmpersand(input)) return input;
        if (input.Contains(@"\begin{") || input.Contains(@"\matrix") ||
            input.Contains(@"\cases"))
            return input; // an aligning construct is present; assume the & belongs to it

        return input.Contains(@"\\")
            ? @"\begin{align}" + input + @"\end{align}"
            : Regex.Replace(input, @"(?<!\\)&", " ");
    }

    private static bool ContainsBareAmpersand(string input)
        => Regex.IsMatch(input, @"(?<!\\)&");
}
