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
    .numeric { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .muted { color: #667085; }
    .table-wrap { max-height: 520px; overflow: auto; border: 1px solid #e5e7eb; border-radius: 8px; }
    .plot-wrap { min-height: 420px; }
    .plot-svg { width: 100%; height: auto; display: block; }
    .axis { stroke: #64748b; stroke-width: 1; }
    .grid { stroke: #e2e8f0; stroke-width: 1; }
    .tick-label { fill: #475569; font-size: 12px; }
    .axis-label { fill: #334155; font-size: 13px; font-weight: 600; }
    .series-line { fill: none; stroke-width: 2.5; }
    .point { stroke: #fff; stroke-width: 1.5; }
    .plot-empty { color: #667085; padding: 24px 0; }
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
      <h2>Median Search Time by Repository Size</h2>
      <div class="legend">
        <span><i class="swatch grep"></i>Grep</span>
        <span><i class="swatch cold"></i>LSP cold</span>
        <span><i class="swatch warm"></i>LSP warm</span>
      </div>
    </div>
    <div id="chart" class="plot-wrap"></div>
  </section>

  <section class="panel">
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Repository</th>
            <th>Query</th>
            <th class="numeric">kLOC</th>
            <th class="numeric">Grep results</th>
            <th class="numeric">LSP results</th>
            <th class="numeric">Solution load</th>
            <th class="numeric">Grep</th>
            <th class="numeric">LSP cold</th>
            <th class="numeric">LSP warm</th>
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
const seriesDefinitions = [
  { label: 'Grep', field: 'grepMilliseconds', color: '#16a34a' },
  { label: 'LSP cold', field: 'lspColdMilliseconds', color: '#dc2626' },
  { label: 'LSP warm', field: 'lspWarmMilliseconds', color: '#7c3aed' }
];
const svgNamespace = 'http://www.w3.org/2000/svg';

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
      numericCell(formatCount(row.grepResultCount)),
      numericCell(formatCount(row.lspResultCount)),
      numericCell(formatMs(row.solutionLoadMilliseconds)),
      numericCell(formatMs(row.grepMilliseconds)),
      numericCell(formatMs(row.lspColdMilliseconds)),
      numericCell(formatMs(row.lspWarmMilliseconds)));
    rowsBody.append(tr);
  }
}

function renderChart(visibleRows) {
  chart.textContent = '';
  const groups = groupRowsByRepository(visibleRows);
  const pointsBySeries = seriesDefinitions.map(definition => ({
    ...definition,
    points: groups
      .map(group => createPoint(group, definition.field))
      .filter(point => point !== null)
  }));
  const allPoints = pointsBySeries.flatMap(series => series.points);
  if (allPoints.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'plot-empty';
    empty.textContent = 'No timing data for the selected queries.';
    chart.append(empty);
    return;
  }

  chart.append(createPlot(pointsBySeries, allPoints));
}

function groupRowsByRepository(rows) {
  const groups = new Map();
  for (const row of rows) {
    if (row.sourceLineCount === null || row.sourceLineCount === undefined || row.sourceLineCount <= 0) {
      continue;
    }

    const key = `${row.repository}\u0000${row.sourceLineCount}`;
    let group = groups.get(key);
    if (group === undefined) {
      group = { repository: row.repository, sourceLineCount: row.sourceLineCount, rows: [] };
      groups.set(key, group);
    }

    group.rows.push(row);
  }

  return [...groups.values()].sort((left, right) => left.sourceLineCount - right.sourceLineCount || left.repository.localeCompare(right.repository));
}

function createPoint(group, field) {
  const values = group.rows
    .map(row => row[field])
    .filter(value => value !== null && value !== undefined && value > 0)
    .sort((left, right) => left - right);
  if (values.length === 0) {
    return null;
  }

  return {
    repository: group.repository,
    sourceLineCount: group.sourceLineCount,
    kLoc: group.sourceLineCount / 1000,
    milliseconds: median(values),
    sampleCount: values.length
  };
}

function createPlot(pointsBySeries, allPoints) {
  const width = 980;
  const height = 430;
  const margin = { top: 20, right: 28, bottom: 58, left: 82 };
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;
  const xValues = allPoints.map(point => point.kLoc);
  const yValues = allPoints.map(point => point.milliseconds);
  let xMin = Math.min(...xValues);
  let xMax = Math.max(...xValues);
  if (xMin === xMax) {
    xMin = Math.max(0, xMin - 1);
    xMax += 1;
  } else {
    const padding = (xMax - xMin) * 0.06;
    xMin = Math.max(0, xMin - padding);
    xMax += padding;
  }

  const yMin = Math.max(1, Math.min(...yValues) * 0.75);
  const yMax = Math.max(yMin * 1.1, Math.max(...yValues) * 1.25);
  const logYMin = Math.log10(yMin);
  const logYMax = Math.log10(yMax);
  const xScale = value => margin.left + ((value - xMin) / (xMax - xMin)) * plotWidth;
  const yScale = value => margin.top + ((logYMax - Math.log10(value)) / (logYMax - logYMin)) * plotHeight;
  const svg = svgElement('svg', { class: 'plot-svg', viewBox: `0 0 ${width} ${height}`, role: 'img', 'aria-label': 'Search time by repository size' });

  renderGrid(svg, margin, plotWidth, plotHeight, xScale, yScale, xMin, xMax, yMin, yMax);
  for (const series of pointsBySeries) {
    renderSeries(svg, series, xScale, yScale);
  }

  svg.append(svgElement('text', { class: 'axis-label', x: margin.left + plotWidth / 2, y: height - 12, 'text-anchor': 'middle' }, 'Repository size (kLOC)'));
  svg.append(svgElement('text', { class: 'axis-label', x: 18, y: margin.top + plotHeight / 2, 'text-anchor': 'middle', transform: `rotate(-90 18 ${margin.top + plotHeight / 2})` }, 'Time (log scale)'));
  return svg;
}

function renderGrid(svg, margin, plotWidth, plotHeight, xScale, yScale, xMin, xMax, yMin, yMax) {
  const xTicks = createLinearTicks(xMin, xMax, 5);
  const yTicks = createLogTicks(yMin, yMax);
  for (const tick of xTicks) {
    const x = xScale(tick);
    svg.append(svgElement('line', { class: 'grid', x1: x, y1: margin.top, x2: x, y2: margin.top + plotHeight }));
    svg.append(svgElement('text', { class: 'tick-label', x, y: margin.top + plotHeight + 20, 'text-anchor': 'middle' }, formatKloc(tick * 1000)));
  }

  for (const tick of yTicks) {
    const y = yScale(tick);
    svg.append(svgElement('line', { class: 'grid', x1: margin.left, y1: y, x2: margin.left + plotWidth, y2: y }));
    svg.append(svgElement('text', { class: 'tick-label', x: margin.left - 10, y: y + 4, 'text-anchor': 'end' }, formatMs(tick)));
  }

  svg.append(svgElement('line', { class: 'axis', x1: margin.left, y1: margin.top + plotHeight, x2: margin.left + plotWidth, y2: margin.top + plotHeight }));
  svg.append(svgElement('line', { class: 'axis', x1: margin.left, y1: margin.top, x2: margin.left, y2: margin.top + plotHeight }));
}

function renderSeries(svg, series, xScale, yScale) {
  const points = [...series.points].sort((left, right) => left.kLoc - right.kLoc);
  if (points.length >= 2) {
    svg.append(svgElement('polyline', {
      class: 'series-line',
      stroke: series.color,
      points: points.map(point => `${xScale(point.kLoc)},${yScale(point.milliseconds)}`).join(' ')
    }));
  }

  for (const point of points) {
    const circle = svgElement('circle', {
      class: 'point',
      cx: xScale(point.kLoc),
      cy: yScale(point.milliseconds),
      r: 5,
      fill: series.color
    });
    circle.append(svgElement('title', {}, `${series.label}\n${point.repository}\n${formatKloc(point.sourceLineCount)} kLOC\n${formatMs(point.milliseconds)} median across ${point.sampleCount} selected queries`));
    svg.append(circle);
  }
}

function svgElement(name, attributes, text) {
  const element = document.createElementNS(svgNamespace, name);
  for (const [key, value] of Object.entries(attributes)) {
    element.setAttribute(key, value);
  }

  if (text !== undefined) {
    element.textContent = text;
  }

  return element;
}

function createLinearTicks(min, max, count) {
  if (count <= 1) {
    return [min];
  }

  const step = (max - min) / (count - 1);
  return Array.from({ length: count }, (_, index) => min + step * index);
}

function createLogTicks(min, max) {
  const ticks = [];
  const minPower = Math.floor(Math.log10(min));
  const maxPower = Math.ceil(Math.log10(max));
  for (let power = minPower; power <= maxPower; power++) {
    for (const multiplier of [1, 2, 5]) {
      const value = multiplier * Math.pow(10, power);
      if (value >= min && value <= max) {
        ticks.push(value);
      }
    }
  }

  return ticks.length === 0 ? [min, max] : ticks;
}

function median(values) {
  const middle = Math.floor(values.length / 2);
  return values.length % 2 === 0
    ? (values[middle - 1] + values[middle]) / 2
    : values[middle];
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

function formatCount(value) {
  if (value === null || value === undefined) {
    return '';
  }

  return value.toLocaleString();
}

function formatMs(value) {
  if (value === null || value === undefined) {
    return '';
  }

  return value >= 1000
    ? `${(value / 1000).toFixed(1)}s`
    : `${Math.round(value)}ms`;
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
                FormatQuery(query),
                grep?.LineCount,
                (lspWarm ?? lspCold)?.LineCount,
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
        return algorithms.FirstOrDefault(algorithm => IsExactLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
          ?? algorithms.FirstOrDefault(algorithm => IsPatternLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
          ?? algorithms.FirstOrDefault(algorithm => IsWorkspaceSymbolLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
          ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsExactLspAlgorithm(algorithm.Name)) : null)
          ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsPatternLspAlgorithm(algorithm.Name)) : null)
          ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsWorkspaceSymbolLspAlgorithm(algorithm.Name)) : null);
    }

    private static bool IsExactLspAlgorithm(string name) =>
      name.Contains("roslyn", StringComparison.OrdinalIgnoreCase) &&
      !name.Contains("with-pattern", StringComparison.OrdinalIgnoreCase) &&
      !name.Contains("workspaceSymbol", StringComparison.OrdinalIgnoreCase);

    private static bool IsPatternLspAlgorithm(string name) =>
      name.Contains("with-pattern", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkspaceSymbolLspAlgorithm(string name) =>
      name.Contains("workspaceSymbol", StringComparison.OrdinalIgnoreCase);

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
        string Query,
        int? GrepResultCount,
        int? LspResultCount,
        double? SolutionLoadMilliseconds,
        double? GrepMilliseconds,
        double? LspColdMilliseconds,
        double? LspWarmMilliseconds);
}
