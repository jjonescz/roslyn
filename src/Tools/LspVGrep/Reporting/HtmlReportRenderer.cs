using System.Net;
using System.Text;
using LspVGrepTool.Models;

namespace LspVGrepTool.Reporting;

internal static class HtmlReportRenderer
{
    public static string Render(ToolReport report)
    {
        s_expandCounter = 0;
        var builder = new StringBuilder();

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <title>LspVGrepTool Report</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: Arial, sans-serif; margin: 2rem; color: #1f2937; }");
        builder.AppendLine("    h1, h2, h3 { margin-bottom: 0.5rem; }");
        builder.AppendLine("    .actions { display: flex; gap: 0.5rem; margin: 1rem 0; }");
        builder.AppendLine("    button { border: 1px solid #b8c2d1; border-radius: 6px; background: #fff; color: #1f2937; padding: 0.35rem 0.65rem; cursor: pointer; }");
        builder.AppendLine("    button:hover { background: #eef2f7; }");
        builder.AppendLine("    details { background: #fff; }");
        builder.AppendLine("    summary { cursor: pointer; }");
        builder.AppendLine("    .query { border: 1px solid #d1d5db; border-radius: 8px; padding: 0.75rem 1rem; margin-bottom: 1rem; }");
        builder.AppendLine("    .query[open] > summary { border-bottom: 1px solid #e5e7eb; margin-bottom: 0.75rem; padding-bottom: 0.75rem; }");
        builder.AppendLine("    .algorithm { border-top: 1px solid #e5e7eb; padding-top: 0.75rem; margin-top: 0.75rem; }");
        builder.AppendLine("    .algorithm[open] > summary { margin-bottom: 0.5rem; }");
        builder.AppendLine("    .summary-title { font-weight: 700; }");
        builder.AppendLine("    .summary-meta { color: #4b5563; margin-left: 0.75rem; font-size: 0.92rem; }");
        builder.AppendLine("    .algorithm-details { margin: 0.5rem 0; color: #4b5563; }");
        builder.AppendLine("    .status { font-weight: bold; }");
        builder.AppendLine("    .status-failed { color: #b91c1c; }");
        builder.AppendLine("    .status-succeeded { color: #047857; }");
        builder.AppendLine("    dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.5rem 1rem; }");
        builder.AppendLine("    dt { font-weight: bold; }");
        builder.AppendLine("    pre { background: #f9fafb; border-radius: 6px; padding: 1rem; overflow-x: auto; white-space: pre-wrap; }");
        builder.AppendLine("    .truncated-link { color: #2563eb; cursor: pointer; font-style: italic; }");
        builder.AppendLine("    .truncated-link:hover { text-decoration: underline; }");
        builder.AppendLine("    .full-result { display: none; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <h1>LspVGrepTool Report</h1>");
        builder.AppendLine($"  <p><strong>Directory:</strong> {Encode(report.Directory)}</p>");
        if (!string.IsNullOrWhiteSpace(report.RepositoryCommitHash))
        {
            builder.AppendLine($"  <p><strong>Commit:</strong> {Encode(report.RepositoryCommitHash)}</p>");
        }

        builder.AppendLine($"  <p><strong>LS cache:</strong> {FormatLsCacheUsage(report)}</p>");

        if (!string.IsNullOrWhiteSpace(report.RoslynTargetPath) && !string.IsNullOrWhiteSpace(report.RoslynTargetKind))
        {
            var loadTime = report.RoslynLoadTime.HasValue
                ? report.RoslynLoadTime.Value.TotalSeconds >= 1
                    ? $"{report.RoslynLoadTime.Value.TotalSeconds:F1}s"
                    : $"{report.RoslynLoadTime.Value.TotalMilliseconds:F0}ms"
                : "N/A";

            builder.AppendLine(
                $"  <p><strong>Roslyn target:</strong> {Encode(report.RoslynTargetKind)} - {Encode(report.RoslynTargetPath)} (loaded in {loadTime})</p>");
        }

        if (report.TgrepIndexTime is { } tgrepIndexTime)
        {
            builder.AppendLine($"  <p><strong>tgrep index:</strong> built in {FormatDuration(tgrepIndexTime)}</p>");
        }

        builder.AppendLine("  <div class=\"actions\">");
        builder.AppendLine("    <button type=\"button\" data-action=\"expand-all\">Expand all</button>");
        builder.AppendLine("    <button type=\"button\" data-action=\"collapse-algorithms\">Collapse algorithms</button>");
        builder.AppendLine("    <button type=\"button\" data-action=\"collapse-all\">Collapse all</button>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <section>");

        foreach (var query in report.Queries)
        {
            builder.AppendLine("    <details class=\"query\" open>");
            builder.AppendLine($"      <summary><span class=\"summary-title\">{Encode(FormatQueryTitle(query))}</span><span class=\"summary-meta\">{query.Algorithms.Count} algorithms</span></summary>");
            builder.AppendLine("      <dl>");
            foreach (var field in query.Fields)
            {
                builder.AppendLine($"        <dt>{Encode(field.Key)}</dt><dd>{Encode(field.Value)}</dd>");
            }

            builder.AppendLine("      </dl>");

            foreach (var algorithm in query.Algorithms)
            {
                var statusClass = algorithm.Outcome == AlgorithmOutcome.Succeeded
                    ? "status-succeeded"
                    : "status-failed";

                var summaryDetail = string.IsNullOrEmpty(algorithm.Summary)
                    ? string.Empty
                    : Encode(algorithm.Summary);

                var elapsed = algorithm.ElapsedTime.TotalSeconds >= 1
                    ? $"{algorithm.ElapsedTime.TotalSeconds:F1}s"
                    : $"{algorithm.ElapsedTime.TotalMilliseconds:F0}ms";

                var open = algorithm.Outcome == AlgorithmOutcome.Failed ? " open" : string.Empty;
                builder.AppendLine($"      <details class=\"algorithm\"{open}>");
                builder.AppendLine(
                    $"        <summary><span class=\"summary-title\">{Encode(algorithm.AlgorithmName)}</span><span class=\"summary-meta\"><span class=\"status {statusClass}\">{Encode(algorithm.Outcome.ToString())}</span> | Characters: {algorithm.CharacterCount} | Lines: {algorithm.LineCount} | Time: {elapsed}</span></summary>");
                if (!string.IsNullOrEmpty(summaryDetail))
                {
                    builder.AppendLine($"        <p class=\"algorithm-details\">{summaryDetail}</p>");
                }

                RenderResponseText(builder, algorithm.ResponseText);
                builder.AppendLine("      </details>");
            }

            builder.AppendLine("    </details>");
        }

        builder.AppendLine("  </section>");
        builder.AppendLine("  <script>");
        builder.AppendLine("    document.querySelector('[data-action=\"expand-all\"]').addEventListener('click', () => document.querySelectorAll('details').forEach(details => details.open = true));");
        builder.AppendLine("    document.querySelector('[data-action=\"collapse-algorithms\"]').addEventListener('click', () => document.querySelectorAll('details.algorithm').forEach(details => details.open = false));");
        builder.AppendLine("    document.querySelector('[data-action=\"collapse-all\"]').addEventListener('click', () => document.querySelectorAll('details').forEach(details => details.open = false));");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string FormatQueryTitle(QueryExecutionReport query)
    {
        if (query.Fields.Count == 0)
            return query.Type;

        var fields = string.Join(", ", query.Fields.Select(static field => $"{field.Key}: {field.Value}"));
        return $"{query.Type} ({fields})";
    }

    private static string FormatLsCacheUsage(ToolReport report)
    {
        if (report.LsCacheUsed)
            return "used (enabled)";

        return report.LsCacheEnabled
            ? "not used (enabled)"
            : "not used (disabled)";
    }

    private static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{elapsed.TotalMilliseconds:F0}ms";

    private const int MaxDisplayLines = 10;
    private static int s_expandCounter;

    private static void RenderResponseText(StringBuilder builder, string text)
    {
        var lines = text.Split('\n');
        if (lines.Length <= MaxDisplayLines)
        {
            builder.AppendLine($"        <pre>{Encode(text)}</pre>");
            return;
        }

        var id = $"expand-{s_expandCounter++}";
        var truncated = string.Join("\n", lines.Take(MaxDisplayLines));
        var remaining = lines.Length - MaxDisplayLines;

        builder.AppendLine($"        <pre id=\"{id}-short\">{Encode(truncated)}");
        builder.AppendLine($"<span class=\"truncated-link\" onclick=\"document.getElementById('{id}-short').style.display='none'; document.getElementById('{id}-full').style.display='block';\">... truncated ({remaining} more lines) — click to expand</span></pre>");
        builder.AppendLine($"        <pre id=\"{id}-full\" class=\"full-result\">{Encode(text)}</pre>");
    }
}
