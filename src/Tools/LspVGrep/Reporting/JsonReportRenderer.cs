using System.Text.Json;
using LspVGrepTool.Models;

namespace LspVGrepTool.Reporting;

internal static class JsonReportRenderer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true
    };

    public static string Render(ToolReport report)
    {
        var jsonReport = JsonSummaryReport.FromToolReport(report);
        return JsonSerializer.Serialize(jsonReport, s_options);
    }
}
