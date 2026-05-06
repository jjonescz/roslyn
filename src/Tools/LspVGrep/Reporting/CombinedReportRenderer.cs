using System.Text.Encodings.Web;
using System.Text.Json;
using LspVGrepTool.Models;

namespace LspVGrepTool.Reporting;

internal static class CombinedReportRenderer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Render(IReadOnlyList<JsonSummaryReport> reports)
    {
        var rows = reports
            .SelectMany(CreateRows)
            .OrderByDescending(static row => row.SourceLineCount ?? -1)
            .ThenBy(static row => row.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Query, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rowsJson = JsonSerializer.Serialize(rows, s_jsonOptions);
        var generatedAt = DateTimeOffset.UtcNow.ToString("u");

        return """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>LspVGrep Combined Report</title>
  <style>
    :root { color-scheme: light; font-family: "Segoe UI", system-ui, sans-serif; }
    body { margin: 0; background: #f7f8fa; color: #1f2937; }
    main { max-width: 1400px; margin: 0 auto; padding: 24px; }
    h1 { margin: 0 0 4px; font-size: 28px; font-weight: 650; }
    .meta { color: #5f6b7a; margin-bottom: 20px; }
    .panel { background: #fff; border: 1px solid #d9dee7; border-radius: 8px; padding: 16px; margin-bottom: 16px; }
    .filters { display: flex; flex-wrap: wrap; gap: 8px; }
    .filters label { display: inline-flex; align-items: center; gap: 6px; border: 1px solid #cbd5e1; border-radius: 6px; padding: 6px 10px; background: #fff; font-size: 13px; }
    .toolbar { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 12px; }
    .toolbar h2 { margin: 0; font-size: 18px; }
    button { border: 1px solid #b8c2d1; border-radius: 6px; background: #fff; color: #1f2937; padding: 6px 10px; cursor: pointer; }
    button:hover { background: #eef2f7; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th, td { padding: 8px 10px; border-bottom: 1px solid #e5e7eb; text-align: left; vertical-align: top; }
    th { background: #f1f5f9; font-weight: 650; position: sticky; top: 0; z-index: 1; }
    td.numeric { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .muted { color: #667085; }
    .table-wrap { max-height: 520px; overflow: auto; border: 1px solid #e5e7eb; border-radius: 8px; }
    .chart { display: grid; gap: 12px; }
    .chart-row { display: grid; grid-template-columns: minmax(180px, 280px) 1fr; gap: 12px; align-items: center; }
    .chart-label { font-size: 12px; color: #334155; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .bar-group { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 6px; align-items: end; min-height: 44px; }
    .bar { min-width: 0; border-radius: 4px; color: #fff; font-size: 11px; line-height: 1.2; padding: 4px; box-sizing: border-box; display: flex; align-items: end; justify-content: center; text-align: center; overflow: hidden; }
    .load { background: #2563eb; }
    .grep { background: #16a34a; }
    .cold { background: #dc2626; }
    .warm { background: #7c3aed; }
    .legend { display: flex; flex-wrap: wrap; gap: 12px; color: #475569; font-size: 12px; }
    .legend span { display: inline-flex; align-items: center; gap: 6px; }
    .swatch { width: 10px; height: 10px; border-radius: 2px; display: inline-block; }
  </style>
</head>
<body>
<main>
  <h1>LspVGrep Combined Report</h1>
  <div class="meta">Generated GENERATED_AT from REPORT_COUNT reports.</div>

  <section class="panel">
    <div class="toolbar">
      <h2>Queries</h2>
      <div>
        <button id="selectAll" type="button">Select all</button>
        <button id="selectNone" type="button">Select none</button>
      </div>
    </div>
    <div id="queryFilters" class="filters"></div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Timings</h2>
      <div class="legend">
        <span><i class="swatch load"></i>Load</span>
        <span><i class="swatch grep"></i>Grep</span>
        <span><i class="swatch cold"></i>LSP cold</span>
        <span><i class="swatch warm"></i>LSP warm</span>
      </div>
    </div>
    <div id="chart" class="chart"></div>
  </section>

  <section class="panel">
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Repository</th>
            <th>Query</th>
            <th>kLOC</th>
            <th>LS cache</th>
            <th>Solution load</th>
            <th>Grep</th>
            <th>LSP cold</th>
            <th>LSP warm</th>
          </tr>
        </thead>
        <tbody id="rows"></tbody>
      </table>
    </div>
  </section>
</main>
<script>
const allRows = ROWS_JSON;
const queryFilters = document.getElementById('queryFilters');
const rowsBody = document.getElementById('rows');
const chart = document.getElementById('chart');

const queries = [...new Set(allRows.map(row => row.query))].sort((left, right) => left.localeCompare(right));
for (const query of queries) {
  const label = document.createElement('label');
  const checkbox = document.createElement('input');
  checkbox.type = 'checkbox';
  checkbox.value = query;
  checkbox.checked = true;
  checkbox.addEventListener('change', render);
  label.append(checkbox, document.createTextNode(query));
  queryFilters.append(label);
}

document.getElementById('selectAll').addEventListener('click', () => setAll(true));
document.getElementById('selectNone').addEventListener('click', () => setAll(false));

function setAll(value) {
  for (const checkbox of queryFilters.querySelectorAll('input')) {
    checkbox.checked = value;
  }

  render();
}

function selectedQueries() {
  return new Set([...queryFilters.querySelectorAll('input:checked')].map(checkbox => checkbox.value));
}

function render() {
  const selected = selectedQueries();
  const visibleRows = allRows.filter(row => selected.has(row.query));
  renderTable(visibleRows);
  renderChart(visibleRows);
}

function renderTable(visibleRows) {
  rowsBody.textContent = '';
  for (const row of visibleRows) {
    const tr = document.createElement('tr');
    tr.append(
      cell(row.repository),
      cell(row.query),
      numericCell(formatKloc(row.sourceLineCount)),
      cell(formatLsCache(row)),
      numericCell(formatMs(row.solutionLoadMilliseconds)),
      numericCell(formatMs(row.grepMilliseconds)),
      numericCell(formatMs(row.lspColdMilliseconds)),
      numericCell(formatMs(row.lspWarmMilliseconds)));
    rowsBody.append(tr);
  }
}

function renderChart(visibleRows) {
  chart.textContent = '';
  const values = visibleRows.flatMap(row => [row.solutionLoadMilliseconds, row.grepMilliseconds, row.lspColdMilliseconds, row.lspWarmMilliseconds]).filter(value => value !== null && value !== undefined && value > 0);
  const max = Math.max(1, ...values.map(value => Math.log10(value + 1)));
  for (const row of visibleRows) {
    const chartRow = document.createElement('div');
    chartRow.className = 'chart-row';
    const label = document.createElement('div');
    label.className = 'chart-label';
    label.title = `${row.repository} - ${row.query}`;
    label.textContent = `${row.repository} - ${row.query}`;
    const group = document.createElement('div');
    group.className = 'bar-group';
    group.append(
      bar(row.solutionLoadMilliseconds, max, 'bar load'),
      bar(row.grepMilliseconds, max, 'bar grep'),
      bar(row.lspColdMilliseconds, max, 'bar cold'),
      bar(row.lspWarmMilliseconds, max, 'bar warm'));
    chartRow.append(label, group);
    chart.append(chartRow);
  }
}

function bar(value, max, className) {
  const div = document.createElement('div');
  div.className = className;
  if (value === null || value === undefined) {
    div.style.height = '8px';
    div.textContent = '';
    div.title = 'No data';
    div.style.opacity = '0.18';
    return div;
  }

  const scaled = Math.log10(value + 1) / max;
  div.style.height = `${Math.max(10, Math.round(scaled * 64))}px`;
  div.textContent = formatMs(value);
  div.title = formatMs(value);
  return div;
}

function cell(text) {
  const td = document.createElement('td');
  td.textContent = text ?? '';
  return td;
}

function numericCell(text) {
  const td = cell(text);
  td.className = 'numeric';
  return td;
}

function formatKloc(value) {
  if (value === null || value === undefined) {
    return '';
  }

  return `${(value / 1000).toFixed(1)}`;
}

function formatMs(value) {
  if (value === null || value === undefined) {
    return '';
  }

  return value >= 1000
    ? `${(value / 1000).toFixed(1)}s`
    : `${Math.round(value)}ms`;
}

function formatLsCache(row) {
  if (row.lsCacheUsed) {
    return 'used';
  }

  if (row.lsCacheEnabled) {
    return 'not used';
  }

  return 'disabled';
}

render();
</script>
</body>
</html>
"""
            .Replace("GENERATED_AT", generatedAt, StringComparison.Ordinal)
            .Replace("REPORT_COUNT", reports.Count.ToString(), StringComparison.Ordinal)
            .Replace("ROWS_JSON", rowsJson, StringComparison.Ordinal);
    }

    private static IEnumerable<CombinedReportRow> CreateRows(JsonSummaryReport report)
    {
        var repository = GetRepositoryName(report);
        foreach (var query in report.Queries)
        {
            var successfulAlgorithms = query.Algorithms
                .Where(static algorithm => string.Equals(algorithm.Outcome, "Succeeded", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var grep = SelectGrepAlgorithm(successfulAlgorithms);
            var lspCold = SelectLspAlgorithm(successfulAlgorithms, pass: 1);
            var lspWarm = SelectLspAlgorithm(successfulAlgorithms, pass: 2);

            yield return new CombinedReportRow(
                repository,
                report.Directory,
                report.SourceLineCount,
                report.LsCache?.Enabled,
                report.LsCache?.Used,
                FormatQuery(query),
                report.RoslynTarget?.LoadTimeMilliseconds,
                grep?.ElapsedMilliseconds,
                lspCold?.ElapsedMilliseconds,
                lspWarm?.ElapsedMilliseconds);
        }
    }

    private static JsonSummaryAlgorithm? SelectGrepAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms) =>
        algorithms.FirstOrDefault(static algorithm =>
            algorithm.Name.Contains("grep", StringComparison.OrdinalIgnoreCase) &&
            !algorithm.Name.Contains("nameonly", StringComparison.OrdinalIgnoreCase))
        ?? algorithms.FirstOrDefault(static algorithm => algorithm.Name.Contains("grep", StringComparison.OrdinalIgnoreCase));

    private static JsonSummaryAlgorithm? SelectLspAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms, int pass)
    {
        var passText = $"(pass {pass})";
        return algorithms.FirstOrDefault(algorithm => IsPreferredLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
            ?? algorithms.FirstOrDefault(algorithm => IsFallbackLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
            ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsPreferredLspAlgorithm(algorithm.Name)) : null)
            ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsFallbackLspAlgorithm(algorithm.Name)) : null);
    }

    private static bool IsPreferredLspAlgorithm(string name) =>
        name.Contains("workspaceSymbol", StringComparison.OrdinalIgnoreCase);

    private static bool IsFallbackLspAlgorithm(string name) =>
        name.Contains("with-pattern", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("roslyn", StringComparison.OrdinalIgnoreCase);

    private static string FormatQuery(JsonSummaryQuery query)
    {
        if (query.Fields.Count == 0)
            return query.Type;

        var fields = string.Join(", ", query.Fields.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}: {pair.Value}"));
        return $"{query.Type} ({fields})";
    }

    private static string GetRepositoryName(JsonSummaryReport report)
    {
        var directory = report.Directory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(directory);
        return string.IsNullOrWhiteSpace(name)
            ? report.Directory
            : name;
    }

    private sealed record CombinedReportRow(
        string Repository,
        string Directory,
        long? SourceLineCount,
        bool? LsCacheEnabled,
        bool? LsCacheUsed,
        string Query,
        double? SolutionLoadMilliseconds,
        double? GrepMilliseconds,
        double? LspColdMilliseconds,
        double? LspWarmMilliseconds);
}
