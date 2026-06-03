using System.Globalization;
using System.Text;
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

    public static string Render(IReadOnlyList<JsonSummaryReport> reports, string outputDirectory, string? rawTimingCsvHref)
    {
        var repositories = reports
          .Select(report => CreateRepositoryRow(report, outputDirectory))
          .OrderBy(static row => row.SourceLineCount ?? long.MaxValue)
          .ThenBy(static row => row.Repository, StringComparer.OrdinalIgnoreCase)
          .ToList();
        var rows = reports
          .SelectMany(report => CreateRows(report, outputDirectory))
          .OrderBy(static row => row.SourceLineCount ?? long.MaxValue)
          .ThenBy(static row => row.Repository, StringComparer.OrdinalIgnoreCase)
          .ThenBy(static row => row.Query, StringComparer.OrdinalIgnoreCase)
          .ToList();

        var repositoriesJson = JsonSerializer.Serialize(repositories, s_jsonOptions);
        var rowsJson = JsonSerializer.Serialize(rows, s_jsonOptions);
        var generatedAt = DateTimeOffset.UtcNow.ToString("u");
        var rawTimingCsvLink = string.IsNullOrWhiteSpace(rawTimingCsvHref)
          ? ""
          : $" <a href=\"{HtmlEncoder.Default.Encode(rawTimingCsvHref)}\">Raw timing CSV</a>.";

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
    .toolbar-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; align-items: center; gap: 12px; }
    a { color: #2563eb; text-decoration: none; }
    a:hover { text-decoration: underline; }
    button { border: 1px solid #b8c2d1; border-radius: 6px; background: #fff; color: #1f2937; padding: 6px 10px; cursor: pointer; }
    button:hover { background: #eef2f7; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th, td { padding: 8px 10px; border-bottom: 1px solid #e5e7eb; text-align: left; vertical-align: top; }
    th { background: #f1f5f9; font-weight: 650; position: sticky; top: 0; z-index: 1; }
    .numeric { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .url-cell { word-break: break-all; }
    .muted { color: #667085; }
    .table-wrap { max-height: 520px; overflow: auto; border: 1px solid #e5e7eb; border-radius: 8px; }
    .plot-wrap { min-height: 420px; }
    .plot-svg { width: 100%; height: auto; display: block; }
    .axis { stroke: #64748b; stroke-width: 1; }
    .grid { stroke: #e2e8f0; stroke-width: 1; }
    .tick-label { fill: #475569; font-size: 12px; }
    .axis-label { fill: #334155; font-size: 13px; font-weight: 600; }
    .series-line { fill: none; stroke-width: 2.75; }
    .point { stroke: #fff; stroke-width: 1.5; }
    .plot-empty { color: #667085; padding: 24px 0; }
    .grep { background: #0072b2; color: #0072b2; }
    .tgrep { background: #e69f00; color: #e69f00; }
    .tgrep-load { background: #d55e00; color: #d55e00; }
    .lsp { background: #009e73; color: #009e73; }
    .current-lsp { background: #7c3aed; color: #7c3aed; }
    .lsp-load { background: #cc79a7; color: #cc79a7; }
    .chart-controls { display: flex; flex-wrap: wrap; justify-content: flex-end; align-items: center; gap: 12px; }
    .legend { display: flex; flex-wrap: wrap; gap: 12px; color: #475569; font-size: 12px; }
    .legend span { display: inline-flex; align-items: center; gap: 6px; }
    .swatch { width: 18px; height: 4px; border-radius: 999px; display: inline-block; }
    .swatch.dashed { background: repeating-linear-gradient(90deg, currentColor 0 6px, transparent 6px 10px); }
    .scale-toggle { display: inline-flex; border: 1px solid #b8c2d1; border-radius: 6px; overflow: hidden; }
    .scale-toggle button { border: 0; border-radius: 0; padding: 5px 10px; }
    .scale-toggle button + button { border-left: 1px solid #b8c2d1; }
    .scale-toggle button[aria-pressed="true"] { background: #2563eb; color: #fff; }
    .scale-toggle button[aria-pressed="true"]:hover { background: #1d4ed8; }
  </style>
</head>
<body>
<main>
  <h1>LspVGrep Combined Report</h1>
  <div class="meta">Generated GENERATED_AT from REPORT_COUNT reports across REPOSITORY_COUNT repositories.CSV_EXPORT_LINK</div>

  <section class="panel">
    <div class="toolbar">
      <h2>Main Results</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th class="numeric">Exact-count accuracy</th>
            <th class="numeric">Accuracy samples</th>
            <th class="numeric">Warm query median</th>
            <th class="numeric">Timing samples</th>
            <th class="numeric">Cold-start median</th>
          </tr>
        </thead>
        <tbody id="mainSummaryRows"></tbody>
      </table>
    </div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Repositories (REPOSITORY_COUNT)</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Repository</th>
            <th>URL</th>
            <th class="numeric">kLOC</th>
          </tr>
        </thead>
        <tbody id="repositoryRows"></tbody>
      </table>
    </div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Queries</h2>
      <div class="toolbar-actions">
        <div class="scale-toggle" role="group" aria-label="Query filter granularity">
          <button id="fineQueryFilter" type="button" aria-pressed="false">Fine</button>
          <button id="coarseQueryFilter" type="button" aria-pressed="true">Coarse</button>
        </div>
        <div>
          <button id="selectAll" type="button">Select all</button>
          <button id="selectNone" type="button">Select none</button>
        </div>
      </div>
    </div>
    <div id="queryFilters" class="filters"></div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Median Search Time by Repository Size</h2>
      <div class="chart-controls">
        <div class="legend">
          <span><i class="swatch grep"></i>Grep</span>
          <span><i class="swatch tgrep"></i>tgrep</span>
          <span><i class="swatch dashed tgrep-load"></i>tgrep + index</span>
          <span><i class="swatch lsp"></i>Ideal LSP</span>
          <span><i class="swatch dashed lsp-load"></i>Ideal LSP + solution load</span>
        </div>
        <div class="scale-toggle" role="group" aria-label="X-axis scale">
          <button id="normalXScale" type="button" aria-pressed="false">X normal</button>
          <button id="logXScale" type="button" aria-pressed="true">X log</button>
        </div>
        <div class="scale-toggle" role="group" aria-label="Y-axis scale">
          <button id="normalScale" type="button" aria-pressed="false">Y normal</button>
          <button id="logScale" type="button" aria-pressed="true">Y log</button>
        </div>
      </div>
    </div>
    <div id="chart" class="plot-wrap"></div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Timing Summary</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Repository</th>
            <th class="numeric">kLOC</th>
            <th class="numeric">Rows</th>
            <th class="numeric">Grep</th>
            <th class="numeric">tgrep</th>
            <th class="numeric">tgrep + index</th>
            <th class="numeric">Ideal LSP</th>
            <th class="numeric">Ideal LSP + solution load</th>
            <th class="numeric">Current LSP</th>
            <th class="numeric">Current LSP + solution load</th>
          </tr>
        </thead>
        <tbody id="timingSummaryRows"></tbody>
      </table>
    </div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2 id="accuracyChartTitle">Mean Accuracy by Repository Size</h2>
      <div class="chart-controls">
        <div class="legend">
          <span><i class="swatch grep"></i>Grep</span>
          <span><i class="swatch tgrep"></i>tgrep</span>
          <span><i class="swatch lsp"></i>Ideal LSP</span>
          <span><i class="swatch current-lsp"></i>Current LSP</span>
        </div>
      </div>
    </div>
    <div id="accuracyChart" class="plot-wrap"></div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Accuracy Summary</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Repository</th>
            <th class="numeric">kLOC</th>
            <th class="numeric">Queries</th>
            <th class="numeric">Grep</th>
            <th class="numeric">tgrep</th>
            <th class="numeric">Ideal LSP</th>
            <th class="numeric">Current LSP</th>
          </tr>
        </thead>
        <tbody id="accuracySummaryRows"></tbody>
      </table>
    </div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>By Query Type</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Query type</th>
            <th class="numeric">Rows</th>
            <th class="numeric">Grep accuracy</th>
            <th class="numeric">tgrep accuracy</th>
            <th class="numeric">Ideal LSP accuracy</th>
            <th class="numeric">Current LSP accuracy</th>
            <th class="numeric">Grep median</th>
            <th class="numeric">tgrep median</th>
            <th class="numeric">Ideal LSP median</th>
            <th class="numeric">Current LSP median</th>
          </tr>
        </thead>
        <tbody id="queryTypeSummaryRows"></tbody>
      </table>
    </div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Detailed Results</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Repository</th>
            <th>Query</th>
            <th class="numeric">kLOC</th>
            <th class="numeric">Expected results</th>
            <th class="numeric">Grep results</th>
            <th class="numeric">tgrep results</th>
            <th class="numeric">Ideal LSP results</th>
            <th class="numeric">Current LSP results</th>
            <th class="numeric">Solution load</th>
            <th class="numeric">tgrep index</th>
            <th class="numeric">Grep</th>
            <th class="numeric">tgrep</th>
            <th class="numeric">Ideal LSP</th>
            <th class="numeric">Current LSP</th>
          </tr>
        </thead>
        <tbody id="rows"></tbody>
      </table>
    </div>
  </section>

  <section class="panel">
    <div class="toolbar">
      <h2>Algorithm Selection</h2>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Query type</th>
            <th>Grep</th>
            <th>tgrep</th>
            <th>Ideal LSP</th>
            <th>Current LSP</th>
          </tr>
        </thead>
        <tbody id="selectionRows"></tbody>
      </table>
    </div>
  </section>
</main>
<script>
const repositories = REPOSITORIES_JSON;
const allRows = ROWS_JSON;
const queryFilters = document.getElementById('queryFilters');
const mainSummaryRowsBody = document.getElementById('mainSummaryRows');
const repositoryRowsBody = document.getElementById('repositoryRows');
const rowsBody = document.getElementById('rows');
const selectionRowsBody = document.getElementById('selectionRows');
const chart = document.getElementById('chart');
const accuracyChart = document.getElementById('accuracyChart');
const timingSummaryRowsBody = document.getElementById('timingSummaryRows');
const accuracySummaryRowsBody = document.getElementById('accuracySummaryRows');
const queryTypeSummaryRowsBody = document.getElementById('queryTypeSummaryRows');
const fineQueryFilterButton = document.getElementById('fineQueryFilter');
const coarseQueryFilterButton = document.getElementById('coarseQueryFilter');
const normalXScaleButton = document.getElementById('normalXScale');
const logXScaleButton = document.getElementById('logXScale');
const normalScaleButton = document.getElementById('normalScale');
const logScaleButton = document.getElementById('logScale');
const seriesDefinitions = [
  { label: 'Grep', field: 'grepMilliseconds', color: '#0072b2' },
  { label: 'tgrep', field: 'tgrepMilliseconds', color: '#e69f00' },
  { label: 'tgrep + index', field: 'tgrepWithIndexMilliseconds', color: '#d55e00', dash: '8 5' },
  { label: 'Ideal LSP', field: 'lspMilliseconds', color: '#009e73' },
  { label: 'Ideal LSP + solution load', field: 'lspWithSolutionLoadMilliseconds', color: '#cc79a7', dash: '8 5' }
];
const accuracySeriesDefinitions = [
  { label: 'Grep', countField: 'grepResultCount', color: '#0072b2' },
  { label: 'tgrep', countField: 'tgrepResultCount', color: '#e69f00' },
  { label: 'Ideal LSP', countField: 'lspResultCount', color: '#009e73' },
  { label: 'Current LSP', countField: 'currentLspResultCount', color: '#7c3aed' }
];
const mainSummaryDefinitions = [
  { label: 'Grep', countField: 'grepResultCount', warmTimeField: 'grepMilliseconds', coldTimeField: null },
  { label: 'tgrep', countField: 'tgrepResultCount', warmTimeField: 'tgrepMilliseconds', coldTimeField: 'tgrepWithIndexMilliseconds' },
  { label: 'Ideal LSP', countField: 'lspResultCount', warmTimeField: 'lspMilliseconds', coldTimeField: 'lspWithSolutionLoadMilliseconds' },
  { label: 'Current LSP', countField: 'currentLspResultCount', warmTimeField: 'currentLspMilliseconds', coldTimeField: 'currentLspWithSolutionLoadMilliseconds' }
];
const svgNamespace = 'http://www.w3.org/2000/svg';
const fineQueries = [...new Set(allRows.map(row => row.query))].sort((left, right) => left.localeCompare(right));
const coarseQueries = [...new Set(allRows.map(row => row.queryType ?? row.query))].sort((left, right) => left.localeCompare(right));
let queryFilterMode = 'coarse';
let xScaleMode = 'log';
let yScaleMode = 'log';

document.getElementById('selectAll').addEventListener('click', () => setAll(true));
document.getElementById('selectNone').addEventListener('click', () => setAll(false));
fineQueryFilterButton.addEventListener('click', () => setQueryFilterMode('fine'));
coarseQueryFilterButton.addEventListener('click', () => setQueryFilterMode('coarse'));
normalXScaleButton.addEventListener('click', () => setXScaleMode('normal'));
logXScaleButton.addEventListener('click', () => setXScaleMode('log'));
normalScaleButton.addEventListener('click', () => setYScaleMode('normal'));
logScaleButton.addEventListener('click', () => setYScaleMode('log'));

function setQueryFilterMode(mode) {
  queryFilterMode = mode;
  fineQueryFilterButton.setAttribute('aria-pressed', String(mode === 'fine'));
  coarseQueryFilterButton.setAttribute('aria-pressed', String(mode === 'coarse'));
  renderQueryFilters();
  render();
}

function setYScaleMode(mode) {
  yScaleMode = mode;
  normalScaleButton.setAttribute('aria-pressed', String(mode === 'normal'));
  logScaleButton.setAttribute('aria-pressed', String(mode === 'log'));
  render();
}

function setXScaleMode(mode) {
  xScaleMode = mode;
  normalXScaleButton.setAttribute('aria-pressed', String(mode === 'normal'));
  logXScaleButton.setAttribute('aria-pressed', String(mode === 'log'));
  render();
}

function setAll(value) {
  for (const checkbox of queryFilters.querySelectorAll('input')) {
    checkbox.checked = value;
  }

  render();
}

function selectedQueries() {
  return new Set([...queryFilters.querySelectorAll('input:checked')].map(checkbox => checkbox.value));
}

function renderQueryFilters() {
  queryFilters.textContent = '';
  const queries = queryFilterMode === 'fine' ? fineQueries : coarseQueries;
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
}

function render() {
  const selected = selectedQueries();
  const visibleRows = allRows.filter(row => selected.has(queryFilterMode === 'fine' ? row.query : (row.queryType ?? row.query)));
  renderMainSummaryTable(visibleRows);
  renderTable(visibleRows);
  renderChart(visibleRows);
  renderAccuracyChart(visibleRows);
  renderTimingSummaryTable(visibleRows);
  renderAccuracySummaryTable(visibleRows);
  renderQueryTypeSummaryTable(visibleRows);
}

function renderTable(visibleRows) {
  rowsBody.textContent = '';
  for (const row of visibleRows) {
    const tr = document.createElement('tr');
    tr.append(
      repositoryCell(row),
      cell(row.query),
      numericCell(formatKloc(row.sourceLineCount)),
      numericCell(formatCount(row.expectedResultCount)),
      numericCell(formatCount(row.grepResultCount)),
      numericCell(formatCount(row.tgrepResultCount)),
      numericCell(formatLspCount(row)),
      numericCell(formatLspCount(row, row.currentLspResultCount)),
      numericCell(formatMs(row.solutionLoadMilliseconds)),
      numericCell(formatMs(row.tgrepIndexMilliseconds)),
      numericCell(formatMs(row.grepMilliseconds)),
      numericCell(formatMs(row.tgrepMilliseconds)),
      numericCell(formatLspMs(row, row.lspMilliseconds)),
      numericCell(formatLspMs(row, row.currentLspMilliseconds)));
    rowsBody.append(tr);
  }
}

function renderMainSummaryTable(visibleRows) {
  mainSummaryRowsBody.textContent = '';
  for (const definition of mainSummaryDefinitions) {
    const accuracy = createAccuracySummary(visibleRows, definition.countField);
    const warmTiming = createTimingSummary(visibleRows, definition.warmTimeField);
    const coldTiming = createTimingSummary(visibleRows, definition.coldTimeField);
    const tr = document.createElement('tr');
    tr.append(
      cell(definition.label),
      numericCell(formatPercent(accuracy.value)),
      numericCell(formatCount(accuracy.sampleCount)),
      numericCell(formatMs(warmTiming.value)),
      numericCell(formatCount(warmTiming.sampleCount)),
      numericCell(formatMs(coldTiming.value)));
    mainSummaryRowsBody.append(tr);
  }
}

function renderTimingSummaryTable(visibleRows) {
  timingSummaryRowsBody.textContent = '';
  for (const group of groupRowsByRepository(visibleRows)) {
    const tr = document.createElement('tr');
    tr.append(
      repositoryCell(group),
      numericCell(formatKloc(group.sourceLineCount)),
      numericCell(formatCount(group.rows.length)),
      numericCell(formatMs(createTimingSummary(group.rows, 'grepMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'tgrepMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'tgrepWithIndexMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'lspMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'lspWithSolutionLoadMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'currentLspMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'currentLspWithSolutionLoadMilliseconds').value)));
    timingSummaryRowsBody.append(tr);
  }
}

function renderAccuracySummaryTable(visibleRows) {
  accuracySummaryRowsBody.textContent = '';
  for (const group of groupRowsByRepository(visibleRows)) {
    const tr = document.createElement('tr');
    tr.append(
      repositoryCell(group),
      numericCell(formatKloc(group.sourceLineCount)),
      numericCell(formatCount(group.rows.length)),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'grepResultCount'))),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'tgrepResultCount'))),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'lspResultCount'))),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'currentLspResultCount'))));
    accuracySummaryRowsBody.append(tr);
  }
}

function renderQueryTypeSummaryTable(visibleRows) {
  queryTypeSummaryRowsBody.textContent = '';
  for (const group of groupRowsByQueryType(visibleRows)) {
    const tr = document.createElement('tr');
    tr.append(
      cell(group.queryType),
      numericCell(formatCount(group.rows.length)),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'grepResultCount'))),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'tgrepResultCount'))),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'lspResultCount'))),
      numericCell(formatAccuracyWithSamples(createAccuracySummary(group.rows, 'currentLspResultCount'))),
      numericCell(formatMs(createTimingSummary(group.rows, 'grepMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'tgrepMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'lspMilliseconds').value)),
      numericCell(formatMs(createTimingSummary(group.rows, 'currentLspMilliseconds').value)));
    queryTypeSummaryRowsBody.append(tr);
  }
}

function renderRepositoryTable() {
  repositoryRowsBody.textContent = '';
  for (const repository of repositories) {
    const tr = document.createElement('tr');
    tr.append(
      repositoryCell(repository),
      urlCell(repository.repositoryUrl),
      numericCell(formatKloc(repository.sourceLineCount)));
    repositoryRowsBody.append(tr);
  }
}

function renderAlgorithmSelectionTable() {
  selectionRowsBody.textContent = '';
  for (const row of getAlgorithmSelectionRows()) {
    const tr = document.createElement('tr');
    tr.append(
      cell(row.queryType),
      cell(row.grepAlgorithms.join(', ')),
      cell(row.tgrepAlgorithms.join(', ')),
      cell(row.lspAlgorithms.join(', ')),
      cell(row.currentLspAlgorithms.join(', ')));
    selectionRowsBody.append(tr);
  }
}

function getAlgorithmSelectionRows() {
  const byQueryType = new Map();
  for (const row of allRows) {
    const queryType = row.queryType ?? row.query;
    let item = byQueryType.get(queryType);
    if (item === undefined) {
      item = { queryType, grepAlgorithms: new Set(), tgrepAlgorithms: new Set(), lspAlgorithms: new Set(), currentLspAlgorithms: new Set() };
      byQueryType.set(queryType, item);
    }

    if (row.grepAlgorithmName) {
      item.grepAlgorithms.add(row.grepAlgorithmName);
    }

    if (row.tgrepAlgorithmName) {
      item.tgrepAlgorithms.add(row.tgrepAlgorithmName);
    }

    if (row.lspAlgorithmName) {
      item.lspAlgorithms.add(row.lspAlgorithmName);
    }

    if (row.currentLspAlgorithmName) {
      item.currentLspAlgorithms.add(row.currentLspAlgorithmName);
    }
  }

  return [...byQueryType.values()]
    .map(item => ({
      queryType: item.queryType,
      grepAlgorithms: [...item.grepAlgorithms].sort((left, right) => left.localeCompare(right)),
      tgrepAlgorithms: [...item.tgrepAlgorithms].sort((left, right) => left.localeCompare(right)),
      lspAlgorithms: [...item.lspAlgorithms].sort((left, right) => left.localeCompare(right)),
      currentLspAlgorithms: [...item.currentLspAlgorithms].sort((left, right) => left.localeCompare(right))
    }))
    .sort((left, right) => left.queryType.localeCompare(right.queryType));
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

  chart.append(createPlot(pointsBySeries, allPoints, xScaleMode, yScaleMode));
}

function renderAccuracyChart(visibleRows) {
  accuracyChart.textContent = '';
  const groups = groupRowsByRepository(visibleRows);
  const pointsBySeries = accuracySeriesDefinitions.map(definition => ({
    ...definition,
    points: groups
      .map(group => createAccuracyPoint(group, definition.countField))
      .filter(point => point !== null)
  }));
  const allPoints = pointsBySeries.flatMap(series => series.points);
  if (allPoints.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'plot-empty';
    empty.textContent = 'No expected-count data for the selected queries.';
    accuracyChart.append(empty);
    return;
  }

  accuracyChart.append(createAccuracyPlot(pointsBySeries, allPoints, xScaleMode));
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
      group = { repository: row.repository, reportHref: row.reportHref, sourceLineCount: row.sourceLineCount, rows: [] };
      groups.set(key, group);
    }

    group.rows.push(row);
  }

  return [...groups.values()].sort((left, right) => left.sourceLineCount - right.sourceLineCount || left.repository.localeCompare(right.repository));
}

function groupRowsByQueryType(rows) {
  const groups = new Map();
  for (const row of rows) {
    const queryType = row.queryType ?? row.query;
    let group = groups.get(queryType);
    if (group === undefined) {
      group = { queryType, rows: [] };
      groups.set(queryType, group);
    }

    group.rows.push(row);
  }

  return [...groups.values()].sort((left, right) => left.queryType.localeCompare(right.queryType));
}

function createTimingSummary(rows, field) {
  if (!field) {
    return { value: null, sampleCount: 0 };
  }

  const values = rows
    .map(row => row[field])
    .filter(value => value !== null && value !== undefined && value > 0)
    .sort((left, right) => left - right);
  return {
    value: values.length === 0 ? null : median(values),
    sampleCount: values.length
  };
}

function createAccuracySummary(rows, countField) {
  if (!countField) {
    return { value: null, sampleCount: 0 };
  }

  const values = rows
    .map(row => computeAccuracy(row.expectedResultCount, row[countField]))
    .filter(value => value !== null && value !== undefined);
  return {
    value: values.length === 0 ? null : average(values),
    sampleCount: values.length
  };
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

function createAccuracyPoint(group, countField) {
  const values = group.rows
    .map(row => computeAccuracy(row.expectedResultCount, row[countField]))
    .filter(value => value !== null && value !== undefined);
  if (values.length === 0) {
    return null;
  }

  return {
    repository: group.repository,
    sourceLineCount: group.sourceLineCount,
    kLoc: group.sourceLineCount / 1000,
    value: average(values),
    sampleCount: values.length
  };
}

function computeAccuracy(expectedCount, actualCount) {
  if (expectedCount === null || expectedCount === undefined || actualCount === null || actualCount === undefined) {
    return null;
  }

  return actualCount === expectedCount ? 1 : 0;
}

function createPlot(pointsBySeries, allPoints, xScaleMode, yScaleMode) {
  const width = 980;
  const height = 430;
  const margin = { top: 20, right: 28, bottom: 58, left: 82 };
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;
  const xValues = allPoints.map(point => point.kLoc);
  const yValues = allPoints.map(point => point.milliseconds);
  const isLogXScale = xScaleMode === 'log';
  const xExtent = createDomain(xValues, isLogXScale);
  const xMin = xExtent.min;
  const xMax = xExtent.max;

  const isLogYScale = yScaleMode === 'log';
  const yMin = isLogYScale ? Math.max(1, Math.min(...yValues) * 0.75) : 0;
  const yMax = Math.max(yMin * 1.1, Math.max(...yValues) * 1.25);
  const xScale = isLogXScale
    ? value => {
        const logXMin = Math.log10(xMin);
        const logXMax = Math.log10(xMax);
        return margin.left + ((Math.log10(value) - logXMin) / (logXMax - logXMin)) * plotWidth;
      }
    : value => margin.left + ((value - xMin) / (xMax - xMin)) * plotWidth;
  const yScale = isLogYScale
    ? value => {
        const logYMin = Math.log10(yMin);
        const logYMax = Math.log10(yMax);
        return margin.top + ((logYMax - Math.log10(value)) / (logYMax - logYMin)) * plotHeight;
      }
    : value => margin.top + ((yMax - value) / (yMax - yMin)) * plotHeight;
  const svg = svgElement('svg', { class: 'plot-svg', viewBox: `0 0 ${width} ${height}`, role: 'img', 'aria-label': `Search time by repository size, x-axis ${xScaleMode} scale, y-axis ${yScaleMode} scale` });

  renderGrid(svg, margin, plotWidth, plotHeight, xScale, yScale, xMin, xMax, yMin, yMax, xScaleMode, yScaleMode);
  for (const series of pointsBySeries) {
    renderSeries(svg, series, xScale, yScale);
  }

  svg.append(svgElement('text', { class: 'axis-label', x: margin.left + plotWidth / 2, y: height - 12, 'text-anchor': 'middle' }, isLogXScale ? 'Repository size (log kLOC)' : 'Repository size (kLOC)'));
  svg.append(svgElement('text', { class: 'axis-label', x: 18, y: margin.top + plotHeight / 2, 'text-anchor': 'middle', transform: `rotate(-90 18 ${margin.top + plotHeight / 2})` }, isLogYScale ? 'Time (log scale)' : 'Time (normal scale)'));
  return svg;
}

function createAccuracyPlot(pointsBySeries, allPoints, xScaleMode) {
  const width = 980;
  const height = 430;
  const margin = { top: 20, right: 28, bottom: 58, left: 82 };
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;
  const xValues = allPoints.map(point => point.kLoc);
  const isLogXScale = xScaleMode === 'log';
  const xExtent = createDomain(xValues, isLogXScale);
  const xMin = xExtent.min;
  const xMax = xExtent.max;
  const xScale = isLogXScale
    ? value => {
        const logXMin = Math.log10(xMin);
        const logXMax = Math.log10(xMax);
        return margin.left + ((Math.log10(value) - logXMin) / (logXMax - logXMin)) * plotWidth;
      }
    : value => margin.left + ((value - xMin) / (xMax - xMin)) * plotWidth;
  const yScale = value => margin.top + (1 - value) * plotHeight;
  const svg = svgElement('svg', { class: 'plot-svg', viewBox: `0 0 ${width} ${height}`, role: 'img', 'aria-label': `Accuracy by repository size, x-axis ${xScaleMode} scale` });

  renderAccuracyGrid(svg, margin, plotWidth, plotHeight, xScale, yScale, xMin, xMax, xScaleMode);
  for (const series of pointsBySeries) {
    renderAccuracySeries(svg, series, xScale, yScale);
  }

  svg.append(svgElement('text', { class: 'axis-label', x: margin.left + plotWidth / 2, y: height - 12, 'text-anchor': 'middle' }, isLogXScale ? 'Repository size (log kLOC)' : 'Repository size (kLOC)'));
  svg.append(svgElement('text', { class: 'axis-label', x: 18, y: margin.top + plotHeight / 2, 'text-anchor': 'middle', transform: `rotate(-90 18 ${margin.top + plotHeight / 2})` }, 'Accuracy (%)'));
  return svg;
}

function createDomain(values, isLogScale) {
  let min = Math.min(...values);
  let max = Math.max(...values);
  if (min === max) {
    if (isLogScale) {
      min = Math.max(0.1, min / 2);
      max *= 2;
    } else {
      min = Math.max(0, min - 1);
      max += 1;
    }
  } else if (isLogScale) {
    min = Math.max(0.1, min * 0.75);
    max *= 1.25;
  } else {
    const padding = (max - min) * 0.06;
    min = Math.max(0, min - padding);
    max += padding;
  }

  return { min, max };
}

function renderGrid(svg, margin, plotWidth, plotHeight, xScale, yScale, xMin, xMax, yMin, yMax, xScaleMode, yScaleMode) {
  const xTicks = xScaleMode === 'log'
    ? createLogTicks(xMin, xMax)
    : createLinearTicks(xMin, xMax, 5);
  const yTicks = yScaleMode === 'log'
    ? createLogTicks(yMin, yMax)
    : createLinearTicks(yMin, yMax, 6);
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

function renderAccuracyGrid(svg, margin, plotWidth, plotHeight, xScale, yScale, xMin, xMax, xScaleMode) {
  const xTicks = xScaleMode === 'log'
    ? createLogTicks(xMin, xMax)
    : createLinearTicks(xMin, xMax, 5);
  const yTicks = [0, 0.25, 0.5, 0.75, 1];
  for (const tick of xTicks) {
    const x = xScale(tick);
    svg.append(svgElement('line', { class: 'grid', x1: x, y1: margin.top, x2: x, y2: margin.top + plotHeight }));
    svg.append(svgElement('text', { class: 'tick-label', x, y: margin.top + plotHeight + 20, 'text-anchor': 'middle' }, formatKloc(tick * 1000)));
  }

  for (const tick of yTicks) {
    const y = yScale(tick);
    svg.append(svgElement('line', { class: 'grid', x1: margin.left, y1: y, x2: margin.left + plotWidth, y2: y }));
    svg.append(svgElement('text', { class: 'tick-label', x: margin.left - 10, y: y + 4, 'text-anchor': 'end' }, formatPercent(tick)));
  }

  svg.append(svgElement('line', { class: 'axis', x1: margin.left, y1: margin.top + plotHeight, x2: margin.left + plotWidth, y2: margin.top + plotHeight }));
  svg.append(svgElement('line', { class: 'axis', x1: margin.left, y1: margin.top, x2: margin.left, y2: margin.top + plotHeight }));
}

function renderSeries(svg, series, xScale, yScale) {
  const points = [...series.points].sort((left, right) => left.kLoc - right.kLoc);
  if (points.length >= 2) {
    const lineAttributes = {
      class: 'series-line',
      stroke: series.color,
      points: points.map(point => `${xScale(point.kLoc)},${yScale(point.milliseconds)}`).join(' ')
    };
    if (series.dash) {
      lineAttributes['stroke-dasharray'] = series.dash;
    }

    svg.append(svgElement('polyline', lineAttributes));
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

function renderAccuracySeries(svg, series, xScale, yScale) {
  const points = [...series.points].sort((left, right) => left.kLoc - right.kLoc);
  if (points.length >= 2) {
    svg.append(svgElement('polyline', {
      class: 'series-line',
      stroke: series.color,
      points: points.map(point => `${xScale(point.kLoc)},${yScale(point.value)}`).join(' ')
    }));
  }

  for (const point of points) {
    const circle = svgElement('circle', {
      class: 'point',
      cx: xScale(point.kLoc),
      cy: yScale(point.value),
      r: 5,
      fill: series.color
    });
    circle.append(svgElement('title', {}, `${series.label}\n${point.repository}\n${formatKloc(point.sourceLineCount)} kLOC\n${formatPercent(point.value)} mean accuracy across ${point.sampleCount} selected queries`));
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

function average(values) {
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

function cell(text) {
  const td = document.createElement('td');
  td.textContent = text ?? '';
  return td;
}

function repositoryCell(row) {
  const td = document.createElement('td');
  if (!row.reportHref) {
    td.textContent = row.repository ?? '';
    return td;
  }

  const link = document.createElement('a');
  link.href = row.reportHref;
  link.textContent = row.repository ?? '';
  td.append(link);
  return td;
}

function urlCell(url) {
  const td = document.createElement('td');
  td.className = 'url-cell';
  if (!url) {
    return td;
  }

  const link = document.createElement('a');
  link.href = url;
  link.textContent = url;
  td.append(link);
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

function formatPercent(value) {
  if (value === null || value === undefined) {
    return '';
  }

  return `${Math.round(value * 100)}%`;
}

function formatAccuracyWithSamples(summary) {
  if (summary.value === null || summary.value === undefined || summary.sampleCount === 0) {
    return '';
  }

  return `${formatPercent(summary.value)} (${formatCount(summary.sampleCount)})`;
}

function formatLspCount(row, value = row.lspResultCount) {
  return row.roslynWorkspacePartial ? 'partial' : formatCount(value);
}

function formatLspMs(row, value) {
  return row.roslynWorkspacePartial ? 'partial' : formatMs(value);
}

renderQueryFilters();
renderRepositoryTable();
renderAlgorithmSelectionTable();
render();
</script>
</body>
</html>
"""
            .Replace("GENERATED_AT", generatedAt, StringComparison.Ordinal)
            .Replace("REPORT_COUNT", reports.Count.ToString(), StringComparison.Ordinal)
            .Replace("REPOSITORY_COUNT", repositories.Count.ToString(), StringComparison.Ordinal)
            .Replace("CSV_EXPORT_LINK", rawTimingCsvLink, StringComparison.Ordinal)
            .Replace("REPOSITORIES_JSON", repositoriesJson, StringComparison.Ordinal)
            .Replace("ROWS_JSON", rowsJson, StringComparison.Ordinal);
    }

    public static string RenderRawTimingCsv(IReadOnlyList<JsonSummaryReport> reports, string outputDirectory)
    {
        var rows = reports
          .SelectMany(report => CreateRows(report, outputDirectory))
          .OrderBy(static row => row.SourceLineCount ?? long.MaxValue)
          .ThenBy(static row => row.Repository, StringComparer.OrdinalIgnoreCase)
          .ThenBy(static row => row.Query, StringComparer.OrdinalIgnoreCase)
          .ToList();

        var builder = new StringBuilder();
        AppendCsvRow(
          builder,
          "Repository",
          "ReportHref",
          "Directory",
          "SourceLineCount",
          "KLoc",
          "Query",
          "QueryType",
          "ExpectedResultCount",
          "GrepAlgorithmName",
          "TgrepAlgorithmName",
          "IdealLspAlgorithmName",
          "CurrentLspAlgorithmName",
          "GrepResultCount",
          "TgrepResultCount",
          "IdealLspResultCount",
          "CurrentLspResultCount",
          "SolutionLoadMilliseconds",
          "TgrepIndexMilliseconds",
          "GrepMilliseconds",
          "TgrepMilliseconds",
          "TgrepWithIndexMilliseconds",
          "IdealLspMilliseconds",
          "IdealLspWithSolutionLoadMilliseconds",
          "CurrentLspMilliseconds",
          "CurrentLspWithSolutionLoadMilliseconds",
          "RoslynWorkspacePartial");

        foreach (var row in rows)
        {
            AppendCsvRow(
              builder,
              row.Repository,
              row.ReportHref,
              row.Directory,
              FormatCsvValue(row.SourceLineCount),
              FormatCsvValue(row.SourceLineCount / 1000.0),
              row.Query,
              row.QueryType,
              FormatCsvValue(row.ExpectedResultCount),
              row.GrepAlgorithmName,
              row.TgrepAlgorithmName,
              row.LspAlgorithmName,
              row.CurrentLspAlgorithmName,
              FormatCsvValue(row.GrepResultCount),
              FormatCsvValue(row.TgrepResultCount),
              FormatCsvValue(row.LspResultCount),
              FormatCsvValue(row.CurrentLspResultCount),
              FormatCsvValue(row.SolutionLoadMilliseconds),
              FormatCsvValue(row.TgrepIndexMilliseconds),
              FormatCsvValue(row.GrepMilliseconds),
              FormatCsvValue(row.TgrepMilliseconds),
              FormatCsvValue(row.TgrepWithIndexMilliseconds),
              FormatCsvValue(row.LspMilliseconds),
              FormatCsvValue(row.LspWithSolutionLoadMilliseconds),
              FormatCsvValue(row.CurrentLspMilliseconds),
              FormatCsvValue(row.CurrentLspWithSolutionLoadMilliseconds),
              FormatCsvValue(row.RoslynWorkspacePartial));
        }

        return builder.ToString();
    }

    private static CombinedRepositoryRow CreateRepositoryRow(JsonSummaryReport report, string outputDirectory) =>
      new(
        GetRepositoryName(report),
        GetReportHref(report, outputDirectory),
        report.RepositoryUrl,
        report.Directory,
        report.SourceLineCount);

    private static IEnumerable<CombinedReportRow> CreateRows(JsonSummaryReport report, string outputDirectory)
    {
        var repository = GetRepositoryName(report);
        var reportHref = GetReportHref(report, outputDirectory);
        foreach (var query in report.Queries)
        {
            var successfulAlgorithms = query.Algorithms
                .Where(static algorithm => string.Equals(algorithm.Outcome, "Succeeded", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var grep = SelectGrepAlgorithm(successfulAlgorithms);
            var tgrep = SelectTgrepAlgorithm(successfulAlgorithms);
            var roslynWorkspacePartial = report.RoslynTarget?.IsPartial == true;
            var lspWarm = roslynWorkspacePartial ? null : SelectIdealLspAlgorithm(successfulAlgorithms, pass: 2);
            var lsp = lspWarm ?? (roslynWorkspacePartial ? null : SelectIdealLspAlgorithm(successfulAlgorithms, pass: 1));
            var currentLspWarm = roslynWorkspacePartial ? null : SelectCurrentLspAlgorithm(successfulAlgorithms, query.Type, pass: 2);
            var currentLsp = currentLspWarm ?? (roslynWorkspacePartial ? null : SelectCurrentLspAlgorithm(successfulAlgorithms, query.Type, pass: 1));
            var expectedCount = query.ExpectedCount ?? (roslynWorkspacePartial ? tgrep?.LineCount : lsp?.LineCount);
            var tgrepWithIndex = report.TgrepIndex?.BuildTimeMilliseconds is { } indexMilliseconds && tgrep?.ElapsedMilliseconds is { } tgrepMilliseconds
              ? indexMilliseconds + tgrepMilliseconds
              : (double?)null;
            var lspWithSolutionLoad = report.RoslynTarget?.LoadTimeMilliseconds is { } loadMilliseconds && lsp?.ElapsedMilliseconds is { } lspMilliseconds
                ? loadMilliseconds + lspMilliseconds
                : (double?)null;
            var currentLspWithSolutionLoad = report.RoslynTarget?.LoadTimeMilliseconds is { } currentLoadMilliseconds && currentLsp?.ElapsedMilliseconds is { } currentLspMilliseconds
                ? currentLoadMilliseconds + currentLspMilliseconds
                : (double?)null;

            yield return new CombinedReportRow(
                repository,
                reportHref,
                report.Directory,
                report.SourceLineCount,
                FormatQuery(query),
                query.Type,
                expectedCount,
                grep?.Name,
                tgrep?.Name,
                lsp?.Name,
                currentLsp?.Name,
                grep?.LineCount,
                tgrep?.LineCount,
                lsp?.LineCount,
                currentLsp?.LineCount,
                report.RoslynTarget?.LoadTimeMilliseconds,
                report.TgrepIndex?.BuildTimeMilliseconds,
                grep?.ElapsedMilliseconds,
                tgrep?.ElapsedMilliseconds,
                tgrepWithIndex,
                lsp?.ElapsedMilliseconds,
                lspWithSolutionLoad,
                currentLsp?.ElapsedMilliseconds,
                currentLspWithSolutionLoad,
                roslynWorkspacePartial);
        }
    }

    private static JsonSummaryAlgorithm? SelectGrepAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms) =>
        algorithms.FirstOrDefault(static algorithm =>
            algorithm.Name.Contains("grep", StringComparison.OrdinalIgnoreCase) &&
            !algorithm.Name.Contains("tgrep", StringComparison.OrdinalIgnoreCase) &&
            !algorithm.Name.Contains("nameonly", StringComparison.OrdinalIgnoreCase))
        ?? algorithms.FirstOrDefault(static algorithm =>
            algorithm.Name.Contains("grep", StringComparison.OrdinalIgnoreCase) &&
            !algorithm.Name.Contains("tgrep", StringComparison.OrdinalIgnoreCase));

    private static JsonSummaryAlgorithm? SelectTgrepAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms) =>
        algorithms.FirstOrDefault(static algorithm => IsTgrepAlgorithm(algorithm.Name) && !algorithm.Name.Contains("nameonly", StringComparison.OrdinalIgnoreCase))
        ?? algorithms.FirstOrDefault(static algorithm => IsTgrepAlgorithm(algorithm.Name));

    private static bool IsTgrepAlgorithm(string name) =>
        name.Contains("tgrep", StringComparison.OrdinalIgnoreCase);

    private static JsonSummaryAlgorithm? SelectIdealLspAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms, int pass)
    {
        var passText = $"(pass {pass})";
        return algorithms.FirstOrDefault(algorithm => IsExactLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
          ?? algorithms.FirstOrDefault(algorithm => IsPatternLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
          ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsExactLspAlgorithm(algorithm.Name)) : null)
          ?? (pass == 1 ? algorithms.FirstOrDefault(algorithm => IsPatternLspAlgorithm(algorithm.Name)) : null);
    }

    private static JsonSummaryAlgorithm? SelectCurrentLspAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms, string queryType, int pass)
    {
        if (string.Equals(queryType, QueryTypes.FindTypeDefinition, StringComparison.Ordinal))
            return SelectWorkspaceSymbolLspAlgorithm(algorithms, pass);

        return SelectIdealLspAlgorithm(algorithms, pass);
    }

    private static JsonSummaryAlgorithm? SelectWorkspaceSymbolLspAlgorithm(IReadOnlyList<JsonSummaryAlgorithm> algorithms, int pass)
    {
        var passText = $"(pass {pass})";
        return algorithms.FirstOrDefault(algorithm => IsWorkspaceSymbolLspAlgorithm(algorithm.Name) && algorithm.Name.Contains(passText, StringComparison.OrdinalIgnoreCase))
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

    private static string? GetReportHref(JsonSummaryReport report, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(report.SourcePath))
            return null;

        var htmlPath = Path.ChangeExtension(report.SourcePath, ".html");
        if (string.IsNullOrWhiteSpace(htmlPath) || !File.Exists(htmlPath))
            return null;

        var fullHtmlPath = Path.GetFullPath(htmlPath);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var relativePath = Path.GetRelativePath(fullOutputDirectory, fullHtmlPath);
        if (!Path.IsPathRooted(relativePath) && !relativePath.StartsWith("..", StringComparison.Ordinal))
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

        return new Uri(fullHtmlPath).AbsoluteUri;
    }

    private static void AppendCsvRow(StringBuilder builder, params string?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(EscapeCsvValue(values[i]));
        }

        builder.AppendLine();
    }

    private static string EscapeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.IndexOfAny(['\"', ',', '\r', '\n']) < 0)
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatCsvValue(long? value) =>
      value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string FormatCsvValue(int? value) =>
      value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string FormatCsvValue(double? value) =>
      value?.ToString("G17", CultureInfo.InvariantCulture) ?? "";

    private static string FormatCsvValue(bool value) =>
      value ? "TRUE" : "FALSE";

    private sealed record CombinedReportRow(
        string Repository,
        string? ReportHref,
        string Directory,
        long? SourceLineCount,
        string Query,
        string QueryType,
        int? ExpectedResultCount,
        string? GrepAlgorithmName,
        string? TgrepAlgorithmName,
        string? LspAlgorithmName,
        string? CurrentLspAlgorithmName,
        int? GrepResultCount,
        int? TgrepResultCount,
        int? LspResultCount,
        int? CurrentLspResultCount,
        double? SolutionLoadMilliseconds,
        double? TgrepIndexMilliseconds,
        double? GrepMilliseconds,
        double? TgrepMilliseconds,
        double? TgrepWithIndexMilliseconds,
        double? LspMilliseconds,
        double? LspWithSolutionLoadMilliseconds,
        double? CurrentLspMilliseconds,
        double? CurrentLspWithSolutionLoadMilliseconds,
        bool RoslynWorkspacePartial);

    private sealed record CombinedRepositoryRow(
      string Repository,
      string? ReportHref,
      string? RepositoryUrl,
      string Directory,
      long? SourceLineCount);
}
