using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Models.Blocks;
using OpenNotes.Persistence;

namespace OpenNotes.Export;

public class ExportService : IExportService
{
    private readonly ITaskService _taskService;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        ITaskService taskService,
        WorkspaceRepository workspaceRepo,
        ILogger<ExportService> logger)
    {
        _taskService = taskService;
        _workspaceRepo = workspaceRepo;
        _logger = logger;
    }

    public async Task ExportTaskToMarkdownAsync(TaskItem task, string outputPath, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {task.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Status:** {task.Status}  ");
        sb.AppendLine($"**Priority:** {task.Priority}  ");
        if (task.DueDate.HasValue)
            sb.AppendLine($"**Due:** {task.DueDate:yyyy-MM-dd}  ");
        if (task.Tags.Count > 0)
            sb.AppendLine($"**Tags:** {string.Join(", ", task.Tags)}  ");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(task.Description))
        {
            sb.AppendLine(task.Description);
            sb.AppendLine();
        }

        foreach (var block in task.ContentBlocks.OrderBy(b => b.Order))
        {
            switch (block)
            {
                case TextBlock tb:
                    sb.AppendLine(tb.Content);
                    break;
                case MarkdownBlock mb:
                    sb.AppendLine(mb.Markdown);
                    break;
                case CodeBlock cb:
                    sb.AppendLine($"```{cb.Language}");
                    sb.AppendLine(cb.Code);
                    sb.AppendLine("```");
                    break;
                case LatexBlock lb:
                    sb.AppendLine($"$$\n{lb.Formula}\n$$");
                    break;
                case MermaidBlock mmd:
                    sb.AppendLine("```mermaid");
                    sb.AppendLine(mmd.Definition);
                    sb.AppendLine("```");
                    break;
                case CalloutBlock call:
                    sb.AppendLine($"> **{call.CalloutType}: {call.Title}**  ");
                    sb.AppendLine($"> {call.Content}");
                    break;
            }
            sb.AppendLine();
        }

        if (task.Checklist.Count > 0)
        {
            sb.AppendLine("## Checklist");
            foreach (var item in task.Checklist.OrderBy(c => c.Order))
                sb.AppendLine($"- [{(item.IsCompleted ? "x" : " ")}] {item.Text}");
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct);
        _logger.LogInformation("Exported task {Id} to Markdown: {Path}", task.Id, outputPath);
    }

    public async Task ExportTaskToHtmlAsync(TaskItem task, string outputPath, CancellationToken ct = default)
    {
        var md = new StringBuilder();
        await ExportTaskToMarkdownAsync(task, md);

        var titleHtml = System.Web.HttpUtility.HtmlEncode(task.Title);
        var bodyHtml = ConvertMarkdownToBasicHtml(md.ToString());
        var html = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>" + titleHtml + "</title>"
            + "<style>body{font-family:'Segoe UI',sans-serif;max-width:900px;margin:40px auto;padding:0 20px}"
            + "pre{background:#1e1e2e;color:#d4d4d4;padding:16px;border-radius:6px;overflow-x:auto}"
            + "code{font-family:Consolas,monospace}"
            + "blockquote{border-left:4px solid #7B9CDF;padding:8px 16px;background:#f5f5f5}"
            + "</style></head><body><article>" + bodyHtml + "</article></body></html>";

        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, ct);
    }

    public async Task ExportWorkspaceToZipAsync(Guid workspaceId, string outputPath, CancellationToken ct = default)
    {
        var workspaceRoot = _workspaceRepo.GetWorkspaceFolder(workspaceId);
        if (!Directory.Exists(workspaceRoot))
            throw new DirectoryNotFoundException($"Workspace folder not found: {workspaceRoot}");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        await Task.Run(() => ZipFile.CreateFromDirectory(workspaceRoot, outputPath,
            CompressionLevel.Optimal, includeBaseDirectory: true), ct);

        _logger.LogInformation("Exported workspace {Id} to ZIP: {Path}", workspaceId, outputPath);
    }

    // Minimal builder overload that writes to a StringBuilder
    private Task ExportTaskToMarkdownAsync(TaskItem task, StringBuilder sb)
    {
        var tempPath = Path.GetTempFileName();
        return ExportTaskToMarkdownAsync(task, tempPath)
            .ContinueWith(async _ =>
            {
                var content = await File.ReadAllTextAsync(tempPath);
                sb.Append(content);
                File.Delete(tempPath);
            }).Unwrap();
    }

    private static string ConvertMarkdownToBasicHtml(string md)
    {
        // Very basic conversion for export; real Markdown rendering uses Markdig
        var lines = md.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("# ")) sb.AppendLine($"<h1>{line[2..]}</h1>");
            else if (line.StartsWith("## ")) sb.AppendLine($"<h2>{line[3..]}</h2>");
            else if (line.StartsWith("### ")) sb.AppendLine($"<h3>{line[4..]}</h3>");
            else if (line.StartsWith("- [x] ")) sb.AppendLine($"<li>✓ {line[6..]}</li>");
            else if (line.StartsWith("- [ ] ")) sb.AppendLine($"<li>☐ {line[6..]}</li>");
            else if (string.IsNullOrWhiteSpace(line)) sb.AppendLine("<br>");
            else sb.AppendLine($"<p>{System.Web.HttpUtility.HtmlEncode(line)}</p>");
        }
        return sb.ToString();
    }
}
