import { metrics } from './measurements.mjs';
import { selectedIsolatedRows } from './isolated-measurements.mjs';
import { ENGINE_TYPES, SCENARIOS, TYPE_ORDER, artifactUrl, durationOnly, runUrl, scenarioTitle } from './isolated-contracts.mjs';
import { compact, el, engineName, friendly, matrix, rankChart, scaling } from './isolated-charts.mjs';

const STATUS = Object.freeze({ measured: 'Measured', unsupported: 'Unsupported scenario',
  unsupportedTopology: 'Unsupported execution topology', failed: 'Benchmark failed · no data', missing: 'No verified result',
  resourceIncomplete: 'Measured · incomplete telemetry' });
const ABSENT = Object.freeze({ unsupportedTopology: 'Single-node only', unsupported: 'Not part of this test',
  failed: 'No result in this run', missing: 'No result in this run', measured: 'No value for this metric',
  resourceIncomplete: 'No value for this metric' });
const HEADINGS = Object.freeze(['Engine', 'Nodes', 'Status', 'Selected metric', 'p50', 'p99', 'Attempts', 'Failures', 'GitHub run']);
const EXACT = new Intl.NumberFormat('en', { maximumFractionDigits: 3 });
const OVERVIEW = SCENARIOS.filter(item => item.value !== 'verify-final');

const nodesLabel = count => count + (count === 1 ? ' node' : ' nodes');
const inType = type => name => type === 'all' || ENGINE_TYPES[name] === type;
const metricFor = scenario => durationOnly(scenario) ? 'duration' : 'throughput';
const rowsFor = (projection, scenario, nodeCount, metric = metricFor(scenario)) =>
  selectedIsolatedRows(projection, scenario, nodeCount, 'session', metric, 'all');

function link(document, text, href) {
  const node = el(document, 'a', null, text);
  node.href = href;
  node.rel = 'noopener noreferrer';
  return node;
}

function panel(document, className, title, subtitle) {
  const section = el(document, 'section', 'bench-panel ' + className);
  const head = section.appendChild(el(document, 'header', 'bench-panel-head'));
  head.append(el(document, 'h3', null, title), el(document, 'p', null, subtitle));
  return section;
}

function absent(document, rows, cohort) {
  const groups = new Map();
  for (const row of rows) groups.set(ABSENT[row.status], [...groups.get(ABSENT[row.status]) ?? [], row]);
  const list = el(document, 'dl', 'bench-absent');
  for (const [label, members] of groups) {
    list.append(el(document, 'dt', null, label));
    const names = list.appendChild(el(document, 'dd'));
    for (const row of members) {
      const reason = row.worker?.reason ?? row.detail ?? 'No verified original result for this session.';
      const chip = row.status === 'failed' || row.status === 'missing'
        ? link(document, row.name, row.worker?.job.url ?? runUrl(cohort)) : el(document, 'span', null, row.name);
      chip.title = reason;
      names.append(chip);
    }
  }
  return list;
}

function board(document, rows, metric, selection, cohort) {
  const ranked = rows.filter(row => row.value !== null);
  const section = panel(document, 'bench-board', scenarioTitle(selection.scenario),
    `${metric.title} · ${metric.direction.toLowerCase()} · ${nodesLabel(selection.nodeCount)}`);
  section.append(ranked.length > 0 ? rankChart(document, ranked, metric)
    : el(document, 'p', 'empty-state', 'No engine in this selection recorded this test.'));
  const missing = rows.filter(row => row.value === null);
  if (missing.length > 0) section.append(absent(document, missing, cohort));
  return section;
}

function overview(document, projection, selection) {
  const columns = OVERVIEW.map(item => {
    const metric = metrics[metricFor(item.value)];
    const values = new Map(rowsFor(projection, item.value, selection.nodeCount).map(row => [row.id, row.value]));
    const recorded = [...values.values()].filter(Number.isFinite);
    return { ...item, values, title: scenarioTitle(item.value), unit: metric.unit, higher: metric.higher === true,
      group: item.mixed ? 'mixed' : null, best: metric.higher ? Math.max(...recorded) : Math.min(...recorded) };
  });
  const engines = rowsFor(projection, 'load-million', selection.nodeCount).filter(row => inType(selection.type)(row.name))
    .map(row => ({ name: row.name, values: columns.map(column => column.values.get(row.id) ?? null) }))
    .filter(row => row.values.some(Number.isFinite));
  if (engines.length === 0) return null;
  const groups = TYPE_ORDER.map(type => ({ type, rows: engines.filter(row => ENGINE_TYPES[row.name] === type)
    .sort((left, right) => left.name.localeCompare(right.name)) })).filter(group => group.rows.length > 0);
  const section = panel(document, 'bench-overview', 'Every test at a glance',
    'Throughput in operations per second; index build in seconds. Darker cells are closer to the fastest engine in that column.');
  const scroll = section.appendChild(el(document, 'div', 'table-scroll matrix-scroll'));
  scroll.tabIndex = 0;
  scroll.setAttribute('role', 'region');
  scroll.setAttribute('aria-label', 'Results by test; scroll this region horizontally on narrow screens');
  scroll.append(matrix(document, { caption: `All tests · ${nodesLabel(selection.nodeCount)}`, columns, groups,
    selected: selection.scenario }));
  const legend = section.appendChild(el(document, 'p', 'heat-legend', 'Slower'));
  for (let step = 0; step < 5; step += 1) legend.append(el(document, 'i', 'heat-' + step));
  legend.append('Fastest');
  return section;
}

function topology(document, projection, selection, metric) {
  const three = new Map(selectedIsolatedRows(projection, selection.scenario, 3, 'session', selection.metric, 'all')
    .map(row => [row.name, row.value]));
  const pairs = selectedIsolatedRows(projection, selection.scenario, 1, 'session', selection.metric, 'all')
    .filter(row => inType(selection.type)(row.name) && row.value > 0 && Number.isFinite(three.get(row.name)))
    .map(row => ({ name: row.name, one: row.value, three: three.get(row.name) }));
  if (pairs.length === 0) return null;
  const section = panel(document, 'bench-scale', '1 node → 3 nodes',
    `${scenarioTitle(selection.scenario)} · ${metric.title.toLowerCase()} for engines measured in both topologies.`);
  const legend = section.appendChild(el(document, 'p', 'scale-legend'));
  legend.append(el(document, 'i', 'scale-dot dot-one'), '1 node', el(document, 'i', 'scale-dot dot-three'), '3 nodes');
  section.append(scaling(document, pairs, metric));
  return section;
}

function evidenceCell(document, row, cohort) {
  const cell = el(document, 'td', 'table-links');
  if (row.worker === null) cell.append(link(document, 'Benchmark run', runUrl(cohort)));
  else cell.append(link(document, row.worker.job.conclusion === 'failure' ? 'Failed job' : 'Job', row.worker.job.url),
    link(document, 'Artifact', artifactUrl(cohort, row.worker.artifact)));
  return cell;
}

function table(document, rows, metric, selection, cohort) {
  const node = el(document, 'table');
  node.append(el(document, 'caption', null, `${metric.title} · ${metric.direction} · ${nodesLabel(selection.nodeCount)}`));
  const header = node.appendChild(el(document, 'thead')).appendChild(el(document, 'tr'));
  for (const heading of HEADINGS) header.appendChild(el(document, 'th', null, heading)).scope = 'col';
  const body = node.appendChild(el(document, 'tbody'));
  for (const row of rows) {
    const tr = body.appendChild(el(document, 'tr'));
    tr.dataset.workerId = row.id;
    tr.dataset.value = row.value === null ? '' : String(row.value);
    const value = row.value === null ? '—' : EXACT.format(row.value) + ' ' + metric.unit;
    for (const text of [row.name, row.nodeCount, STATUS[row.status], value, friendly(row.p50, 'ms'), friendly(row.p99, 'ms'),
      row.attempts === null ? null : EXACT.format(row.attempts), row.failures === null ? null : EXACT.format(row.failures)])
      tr.append(el(document, 'td', null, text === null ? '—' : String(text)));
    tr.append(evidenceCell(document, row, cohort));
  }
  const scroll = el(document, 'div', 'table-scroll isolated-table-scroll');
  scroll.tabIndex = 0;
  scroll.setAttribute('role', 'region');
  scroll.setAttribute('aria-label', metric.title + ' results table; scroll this region horizontally on narrow screens');
  scroll.append(node);
  return scroll;
}

function setup(document, rows) {
  const grid = el(document, 'div', 'bench-setup');
  for (const row of rows) {
    if (row.worker?.session.targetProfile == null) continue;
    const target = row.worker.report.targets[0];
    const card = grid.appendChild(el(document, 'dl', 'setup-card'));
    card.dataset.workerId = row.id;
    card.append(engineName(document, row.name, 'div'));
    for (const [name, value] of [['Version', target.version], ['Topology', target.topology],
      ['Data copies', target.cluster.dataCopies], ['Write acknowledgement', target.writeAcknowledgement]]) {
      card.append(el(document, 'dt', null, name), el(document, 'dd', null, String(value)));
    }
  }
  return grid;
}

function footer(document, catalog, projection, rows, metric, selection) {
  const cohort = catalog.cohort;
  const section = el(document, 'footer', 'bench-foot');
  const runtimes = [...new Set(projection.workers.map(({ session: { runtime } }) =>
    `${runtime.operatingSystem} · ${runtime.logicalProcessors} vCPU · ${runtime.framework}`))];
  const meta = section.appendChild(el(document, 'p', 'bench-meta'));
  meta.append(link(document, `GitHub Actions run ${cohort.runId}`, runUrl(cohort)),
    el(document, 'span', null, `${projection.workers.length} of ${projection.plan.workers.length} jobs completed`),
    link(document, 'Source ' + catalog.measuredSourceRevision.slice(0, 7),
      `https://github.com/${cohort.repository}/tree/${catalog.measuredSourceRevision}`),
    ...runtimes.map(text => el(document, 'span', null, text)));
  section.append(el(document, 'p', 'bench-note', 'Every job ran alone on its own GitHub-hosted runner, so hardware can ' +
    'differ between engines. Figures describe this run, not production readiness.'));
  const details = section.appendChild(el(document, 'details', 'bench-details'));
  details.append(el(document, 'summary', null, 'Results table, engine setup and GitHub artifacts'),
    table(document, rows, metric, selection, cohort), setup(document, rows));
  return section;
}

export function renderIsolatedSummary(container, projection) {
  const document = container.ownerDocument;
  const engines = new Set();
  let operations = 0;
  for (const item of SCENARIOS) for (const nodeCount of [1, 3]) for (const row of rowsFor(projection, item.value, nodeCount)) {
    if (row.value === null) continue;
    engines.add(row.name);
    operations += row.attempts ?? 0;
  }
  const records = Math.max(0, ...projection.workers.map(worker => worker.reports[0].expectedRecords));
  container.replaceChildren(...[[String(engines.size), 'databases measured'], [compact(operations, 'ops/s'), 'operations timed'],
    [compact(records, 'ops/s'), 'documents per engine'], [String(projection.options.concurrency), 'concurrent clients']]
    .map(([value, label]) => {
      const tile = el(document, 'div', 'kpi');
      tile.append(el(document, 'strong', null, value), el(document, 'span', null, label));
      return tile;
    }));
  return engines;
}

export function renderNodeCounts(fieldset, projection, selection) {
  for (const input of fieldset.querySelectorAll('input')) {
    const count = rowsFor(projection, selection.scenario, Number(input.value), selection.metric)
      .filter(row => row.value !== null && inType(selection.type)(row.name)).length;
    const label = input.nextElementSibling;
    label.replaceChildren(nodesLabel(Number(input.value)), el(fieldset.ownerDocument, 'small', null, String(count)));
  }
}

export function renderIsolatedView(container, catalog, projection, rows, selection) {
  const document = container.ownerDocument;
  const metric = metrics[selection.metric];
  const visible = rows.filter(row => inType(selection.type)(row.name));
  container.replaceChildren(...[board(document, visible, metric, selection, catalog.cohort), overview(document, projection, selection),
    topology(document, projection, selection, metric), footer(document, catalog, projection, visible, metric, selection)]
    .filter(Boolean));
}
