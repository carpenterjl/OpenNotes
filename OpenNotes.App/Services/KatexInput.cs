namespace OpenNotes.Services;

/// <summary>Minimal input normalization for the KaTeX render path — the successor to the 9-stage
/// <see cref="LatexPreprocessor"/>, which is retained only for the WpfMath PDF fallback. KaTeX
/// natively handles what WpfMath could not (<c>%</c> comments, <c>\mathbf</c>, <c>\boxed</c>,
/// <c>\mathbb</c>, <c>\dfrac</c>, aligned/cases/matrices, Unicode), so only two conveniences
/// remain: stripping user-typed math delimiters (deriving display mode from them) and stacking
/// bare multi-line input.</summary>
public static class KatexInput
{
    /// <summary>Normalize <paramref name="raw"/> for <c>katex.render</c>.
    /// Delimiters: surrounding <c>$$…$$</c> / <c>\[…\]</c> force display mode, <c>$…$</c> /
    /// <c>\(…\)</c> force inline; no delimiters defaults to display (matching the block/canvas
    /// behavior under WpfMath). Bare hard newlines (no <c>\begin{…}</c> present) are joined with
    /// <c>\\</c> and wrapped in <c>\begin{gathered}</c> so multi-line input stacks vertically;
    /// input that already uses an environment passes through untouched.</summary>
    public static string Normalize(string? raw, out bool displayMode)
    {
        displayMode = true;
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.Trim();

        if (TryStrip(s, "$$", "$$", out var inner) || TryStrip(s, @"\[", @"\]", out inner))
        {
            displayMode = true;
            s = inner.Trim();
        }
        else if (TryStrip(s, "$", "$", out inner) || TryStrip(s, @"\(", @"\)", out inner))
        {
            displayMode = false;
            s = inner.Trim();
        }

        // Multi-line without an explicit environment → stack lines like the old WpfMath path did.
        if (!s.Contains(@"\begin{", StringComparison.Ordinal) &&
            (s.Contains('\n') || s.Contains('\r')))
        {
            var lines = s.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 1)
                s = @"\begin{gathered}" + string.Join(@" \\ ", lines) + @"\end{gathered}";
        }

        return s;
    }

    private static bool TryStrip(string s, string open, string close, out string inner)
    {
        inner = string.Empty;
        if (s.Length <= open.Length + close.Length) return false;
        if (!s.StartsWith(open, StringComparison.Ordinal) || !s.EndsWith(close, StringComparison.Ordinal))
            return false;

        inner = s[open.Length..^close.Length];
        // "$x$ + $y$" (or the $$ analogue) must not be treated as one delimited block; only strip
        // when the interior contains no further occurrence of the dollar delimiter.
        return !open.StartsWith('$') || !inner.Contains(open, StringComparison.Ordinal);
    }
}
