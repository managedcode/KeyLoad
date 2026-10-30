import { selectedIsolatedRows } from './isolated-measurements.mjs';
import { ENGINE_TYPES, GENERAL, TESTS, artifactUrl, runUrl } from './isolated-contracts.mjs';
import { NODES, change, compact, el, engineName, friendly, latency, logScale, pair, retained, ticks } from './isolated-charts.mjs';

const STATUS = Object.freeze({ measured: 'Measured', unsupported: 'Unsupported scenario',
  unsupportedTopology: 'Unsupported execution topology', failed: 'Benchmark failed · no data', missing: 'No verified result',
  resourceIncomplete: 'Measured · incomplete telemetry' });
const REASONS = Object.freeze({ unsupportedTopology: 'single-node only', unsupported: 'not in this workload',
  failed: 'no result in this run', missing: 'no result in this run' });
// Table cells round for reading: whole operations per second from 100 up, milliseconds for time tests.
const FINE = new Intl.NumberFormat('en', { maximumFractionDigits: 3 });
const RATE = new Intl.NumberFormat('en', { maximumFractionDigits: 2 });
const WHOLE = new Intl.NumberFormat('en', { maximumFractionDigits: 0 });
const tableValue = (column, value) => (column.seconds ? FINE : value >= 100 ? WHOLE : RATE).format(value);
const COLUMNS = [...TESTS.slice(0, 5), { id: 'mixed', label: 'Mixed total' }, ...TESTS.slice(5)];
const reason = row => REASONS[row.status] ?? 'not recorded';

export function isolatedModel(projection) {
  const cells = new Map();
  const rows = (id, nodeCount, metric) => selectedIsolatedRows(projection, id, nodeCount, 'session', metric, 'all');
  for (const nodeCount of NODES) {
    for (const test of TESTS) for (const row of rows(test.id, nodeCount, test.seconds ? 'duration' : 'throughput'))
      cells.set(`${row.name}|${test.id}|${nodeCount}`, row);
    // Whole-test rate: successful operations of all five concurrent lanes over the mixed test's elapsed time.
    for (const row of rows('mixed', nodeCount, 'duration')) cells.set(`${row.name}|mixed|${nodeCount}`, { ...row,
      value: row.value === null ? null : row.worker.reports.find(report => report.caseId === 'mixed').lanes
        .reduce((sum, lane) => sum + lane.measurement.successes, 0) / row.value });
  }
  const engines = [...new Set(projection.plan.workers.map(worker => worker.target))];
  const cell = (engine, id, nodeCount) => cells.get(`${engine}|${id}|${nodeCount}`);
  const best = new Map(COLUMNS.map(({ id, seconds }) => {
    const values = engines.flatMap(engine => NODES.map(nodeCount => cell(engine, id, nodeCount).value)).filter(Number.isFinite);
    return [id, seconds ? Math.min(...values) : Math.max(...values)];
  }));
  const recorded = (engine, nodeCount) => COLUMNS.some(({ id }) => cell(engine, id, nodeCount).value !== null);
  const measured = engine => NODES.some(nodeCount => recorded(engine, nodeCount));
  // A topology without any recorded test is stated once beside the engine instead of under every bar.
  const nodes = engine => NODES.filter(nodeCount => nodeCount === 1 || recorded(engine, nodeCount));
  return { projection, engines, cell, best, measured, nodes };
}

// Bars compare with the fastest engine of the whole run; a negative change always means slower on 3 nodes.
function items(model, engine, { id, seconds }) {
  const one = model.cell(engine, id, 1).value;
  return model.nodes(engine).map(nodes => {
    const row = model.cell(engine, id, nodes);
    const value = row.value;
    const text = value === null ? '' : seconds ? friendly(value, 's') : compact(value);
    return { nodes, value, text, reason: reason(row), title: value === null ? 'No data' : text + (seconds ? '' : ' ops/s'),
      ratio: value === null ? 0 : seconds ? model.best.get(id) / value : value / model.best.get(id),
      note: nodes === 3 && value !== null && one !== null ? seconds ? change(value, one) : change(one, value) : null };
  });
}

function panel(document, className, title, subtitle) {
  const section = el(document, 'section', 'bench-panel ' + className);
  section.appendChild(el(document, 'header', 'bench-panel-head')).append(el(document, 'h3', null, title), el(document, 'p', null, subtitle));
  return section;
}

function link(document, text, href) {
  const node = el(document, 'a', null, text);
  node.href = href;
  node.rel = 'noopener noreferrer';
  return node;
}

function button(document, className, data, value) {
  const node = el(document, 'button', className);
  node.type = 'button';
  node.dataset[data] = value;
  return node;
}

// Each family card names the highest 1-node result of this run and doubles as the sort control for the list.
function leaders(document, model, sort) {
  const grid = el(document, 'div', 'leaders');
  for (const column of GENERAL.slice(0, 4)) {
    const ranked = model.engines.map(engine => [engine, model.cell(engine, column.id, 1).value])
      .filter(([, value]) => value !== null).sort((left, right) => right[1] - left[1]).slice(0, 5);
    const card = grid.appendChild(button(document, 'leader' + (column.key === sort ? ' is-sorted' : ''), 'sort', column.key));
    card.append(el(document, 'span', 'leader-label', column.label), el(document, 'small', null, column.hint));
    if (ranked.length === 0) {
      card.append(el(document, 'span', 'no-data', 'No data'));
      continue;
    }
    card.append(el(document, 'strong', null, compact(ranked[0][1])), el(document, 'span', 'leader-top', ranked[0][0] + ' · ops/s, 1 node'));
    const bars = card.appendChild(el(document, 'span', 'leader-bars'));
    for (const [engine, value] of ranked) {
      const row = bars.appendChild(el(document, 'span'));
      row.append(el(document, 'small', null, engine));
      row.appendChild(el(document, 'i')).style.setProperty('--size', (value / ranked[0][1] * 100).toFixed(1) + '%');
      row.append(el(document, 'b', null, compact(value)));
    }
  }
  return grid;
}

function engineContracts(document, model, engine) {
  const details = el(document, 'details', 'engine-contracts');
  details.append(el(document, 'summary', null, 'Version, topology and ACK'));
  const facts = details.appendChild(el(document, 'dl'));
  const fact = (label, value) => facts.append(el(document, 'dt', null, label), el(document, 'dd', null, value));
  for (const nodes of NODES) {
    const profile = model.cell(engine, 'load-million', nodes).worker?.session.targetProfile;
    if (!profile) {
      fact(nodes === 1 ? '1 node' : '3 nodes', 'No observed execution profile');
      continue;
    }
    fact(nodes === 1 ? '1-node version' : '3-node version', profile.version);
    fact(nodes === 1 ? '1-node topology' : '3-node topology', profile.topology);
    fact(nodes === 1 ? '1-node ACK' : '3-node ACK', profile.writeAcknowledgement);
    fact(nodes === 1 ? '1-node reads' : '3-node reads', profile.readContract);
  }
  return details;
}

function overview(document, model, sort, type = 'all') {
  const engines = model.engines.filter(engine => type === 'all' || ENGINE_TYPES[engine] === type);
  const leading = new Set(({ Vector: ['vector'], Queue: ['queue'], 'Event store': ['append', 'events'] })[type] ?? []);
  const columns = [...GENERAL.filter(column => leading.has(column.key)), ...GENERAL.filter(column => !leading.has(column.key))];
  const section = panel(document, 'bench-overview', type === 'all' ? 'All databases' : type,
    'Operations per second. Top bar 1 node, lower bar 3 nodes with the change against 1 node. ' +
      (type === 'all' ? 'Click a card or column to sort.' : 'Click a column to sort.'));
  if (type === 'all') section.append(leaders(document, model, sort));
  const scroll = section.appendChild(el(document, 'div', 'comparison-scroll'));
  scroll.tabIndex = 0;
  scroll.setAttribute('role', 'region');
  scroll.setAttribute('aria-label', 'Database comparison table; scroll horizontally to see every operation');
  const grid = scroll.appendChild(el(document, 'table', 'comparison-grid'));
  grid.style.setProperty('--columns', columns.length);
  grid.setAttribute('role', 'table');
  grid.setAttribute('aria-label', type === 'all' ? 'All database comparisons' : type + ' comparisons');
  const head = grid.appendChild(el(document, 'thead')).appendChild(el(document, 'tr', 'ov-head'));
  head.appendChild(el(document, 'th', null, 'Database')).scope = 'col';
  for (const column of columns) {
    const cell = head.appendChild(el(document, 'th'));
    cell.scope = 'col';
    cell.setAttribute('aria-sort', column.key === sort ? 'descending' : 'none');
    cell.appendChild(button(document, column.key === sort ? 'is-sorted' : null, 'sort', column.key))
      .append(column.label, el(document, 'small', null, column.hint));
  }
  const id = GENERAL.find(column => column.key === sort).id;
  const rank = engine => model.cell(engine, id, 1).value ?? (model.measured(engine) ? -1 : -2);
  const list = grid.appendChild(el(document, 'tbody', 'ov-list'));
  for (const engine of [...engines].sort((left, right) => rank(right) - rank(left))) {
    const row = list.appendChild(el(document, 'tr', 'ov-row' + (engine === 'KeyLoad' ? ' is-keyload' : '')));
    row.dataset.target = engine;
    const engineCell = row.appendChild(el(document, 'th'));
    engineCell.scope = 'row';
    const name = engineCell.appendChild(button(document, 'ov-name', 'type', ENGINE_TYPES[engine]));
    name.append(engineName(document, engine));
    engineCell.append(engineContracts(document, model, engine));
    if (model.measured(engine) && model.nodes(engine).length === 1)
      name.append(el(document, 'small', 'no-data', '3 nodes: no data · ' + reason(model.cell(engine, 'load-million', 3))));
    for (const column of columns) {
      const cell = row.appendChild(el(document, 'td')).appendChild(pair(document, items(model, engine, column)));
      cell.dataset.metric = column.key;
    }
  }
  return section;
}

function latencyPanel(document, model, id, key, title) {
  const engines = model.engines.filter(engine => model.cell(engine, id, 1).p50 !== null || model.cell(engine, id, 3).p50 !== null)
    .sort((left, right) => (model.cell(left, id, 1).p50 ?? Infinity) - (model.cell(right, id, 1).p50 ?? Infinity));
  if (engines.length === 0) return null;
  const entries = engine => NODES.map(nodes => ({ nodes, ...model.cell(engine, id, nodes) }));
  const scale = logScale(engines.flatMap(engine => entries(engine).flatMap(entry => [entry.p50, entry.p99])));
  const section = panel(document, 'bench-latency', title, 'Per request on a log scale: dot p50, ring p95, line end p99. Values show p50.');
  section.dataset.metric = key;
  section.append(ticks(document, scale, ''));
  const list = section.appendChild(el(document, 'ul', 'lat-list'));
  for (const engine of engines) {
    const item = list.appendChild(el(document, 'li'));
    item.dataset.target = engine;
    const values = el(document, 'span', 'lat-values');
    for (const entry of entries(engine)) if (entry.p50 !== null) values.append(el(document, 'small', 'n' + entry.nodes, friendly(entry.p50, 'ms')));
    item.append(engineName(document, engine), latency(document, entries(engine), scale), values);
  }
  return section;
}

function cost(document, model) {
  const columns = GENERAL.slice(0, 4);
  const both = engine => columns.some(({ id }) => model.cell(engine, id, 1).value > 0 && model.cell(engine, id, 3).value !== null);
  const engines = model.engines.filter(both);
  if (engines.length === 0) return null;
  const ratio = (engine, id) => (model.cell(engine, id, 3).value ?? 0) / (model.cell(engine, id, 1).value || Infinity);
  const scale = Math.max(1, ...engines.flatMap(engine => columns.map(({ id }) => ratio(engine, id))));
  const section = panel(document, 'bench-cost', 'What three nodes cost',
    '3 replicated nodes against 1 node. Data copies and write acknowledgement differ by engine.');
  const legend = section.querySelector('.bench-panel-head').appendChild(el(document, 'p', 'keep-legend'));
  legend.append(el(document, 'i', 'keep-bar'), '3-node throughput', el(document, 'i', 'keep-tick'), '1-node throughput');
  const head = section.appendChild(el(document, 'div', 'cost-row cost-head'));
  head.append(el(document, 'span', null, 'Database'), ...columns.map(column => el(document, 'span', null, column.label)));
  for (const engine of engines) {
    const row = section.appendChild(el(document, 'div', 'cost-row'));
    row.append(engineName(document, engine));
    for (const { id, key } of columns) {
      row.appendChild(retained(document, model.cell(engine, id, 1).value, model.cell(engine, id, 3).value, scale)).dataset.metric = key;
    }
  }
  return section;
}

// The mixed lanes share one group heading so the table stays narrow enough to read.
function tableHead(document, table) {
  const head = table.appendChild(el(document, 'thead'));
  const [top, lanes] = [head.appendChild(el(document, 'tr')), head.appendChild(el(document, 'tr'))];
  const heading = (row, text, className, span) => {
    const cell = row.appendChild(el(document, 'th', className, text));
    cell.scope = span?.colSpan ? 'colgroup' : 'col';
    Object.assign(cell, span);
  };
  heading(top, 'Engine', 'pin', { rowSpan: 2 });
  for (const text of ['Nodes', 'Status']) heading(top, text, null, { rowSpan: 2 });
  const mixed = COLUMNS.filter(column => column.id.startsWith('mixed'));
  for (const column of COLUMNS) {
    if (column === mixed[0]) heading(top, 'Mixed test', 'num group', { colSpan: mixed.length });
    if (mixed.includes(column)) heading(lanes, column.label.replace('Mixed ', ''), 'num');
    else heading(top, column.label, 'num', { rowSpan: 2 });
  }
  heading(top, 'GitHub run', null, { rowSpan: 2 });
}

function table(document, model, type, cohort) {
  const node = el(document, 'table');
  node.append(el(document, 'caption', null, 'Recorded values per session · operations per second, time tests in seconds'));
  tableHead(document, node);
  const body = node.appendChild(el(document, 'tbody'));
  for (const engine of model.engines.filter(name => type === 'all' || ENGINE_TYPES[name] === type)) for (const nodes of NODES) {
    const session = COLUMNS.map(column => model.cell(engine, column.id, nodes)).find(row => row.value !== null)
      ?? model.cell(engine, 'load-million', nodes);
    const tr = body.appendChild(el(document, 'tr'));
    tr.dataset.workerId = session.id;
    tr.append(el(document, 'td', 'pin', engine), el(document, 'td', null, String(nodes)), el(document, 'td', null, STATUS[session.status]));
    for (const column of COLUMNS) {
      const value = model.cell(engine, column.id, nodes).value;
      const td = tr.appendChild(el(document, 'td', null, value === null ? 'No data' : tableValue(column, value)));
      td.dataset.test = column.id;
      td.dataset.value = value === null ? '' : String(value);
      if (value !== null) td.title = 'Recorded ' + value + (column.seconds ? ' s' : ' operations per second');
    }
    const links = tr.appendChild(el(document, 'td', 'table-links'));
    if (session.worker === null) links.append(link(document, 'Benchmark run', runUrl(cohort)));
    else links.append(link(document, session.worker.job.conclusion === 'failure' ? 'Failed job' : 'Job', session.worker.job.url),
      link(document, 'Artifact', artifactUrl(cohort, session.worker.artifact)));
  }
  const scroll = el(document, 'div', 'table-scroll isolated-table-scroll');
  scroll.tabIndex = 0;
  scroll.setAttribute('role', 'region');
  scroll.setAttribute('aria-label', 'Results table; scroll this region horizontally on narrow screens');
  scroll.append(node);
  return scroll;
}

function footer(document, catalog, model, type) {
  const { cohort, measuredSourceRevision: revision } = catalog;
  const { projection } = model;
  const section = el(document, 'footer', 'bench-foot');
  const runtimes = [...new Set(projection.workers.map(({ session: { runtime } }) =>
    `${runtime.operatingSystem} · ${runtime.logicalProcessors} vCPU · ${runtime.framework}`))];
  section.appendChild(el(document, 'p', 'bench-meta')).append(link(document, `GitHub Actions run ${cohort.runId}`, runUrl(cohort)),
    el(document, 'span', null, `${projection.workers.length} of ${projection.plan.workers.length} jobs completed`),
    link(document, 'Source ' + revision.slice(0, 7), `https://github.com/${cohort.repository}/tree/${revision}`),
    ...runtimes.map(text => el(document, 'span', null, text)));
  section.append(el(document, 'p', 'bench-note', 'Every job ran alone on its own GitHub-hosted runner, so hardware can ' +
    'differ between engines. Figures describe this run, not production readiness.'));
  const details = section.appendChild(el(document, 'details', 'bench-details'));
  details.append(el(document, 'summary', null, 'All recorded values, GitHub jobs and artifacts'), table(document, model, type, cohort));
  return section;
}

export function renderIsolatedSummary(container, model) {
  const document = container.ownerDocument;
  let operations = 0;
  for (const engine of model.engines) for (const test of TESTS) for (const nodes of NODES) {
    const row = model.cell(engine, test.id, nodes);
    if (row.value !== null) operations += row.attempts ?? 0;
  }
  const records = Math.max(0, ...model.projection.workers.map(worker => worker.reports[0].expectedRecords));
  container.replaceChildren(...[[String(model.engines.filter(model.measured).length), 'databases measured'],
    [compact(operations), 'operations timed'], [compact(records), 'records per corpus'],
    [String(model.projection.options.concurrency), 'concurrent clients']].map(([value, label]) => {
    const tile = el(document, 'div', 'kpi');
    tile.append(el(document, 'strong', null, value), el(document, 'span', null, label));
    return tile;
  }));
}

export function renderIsolatedView(container, catalog, model, selection) {
  const document = container.ownerDocument;
  const legend = el(document, 'p', 'bench-legend');
  legend.append(el(document, 'i', 'n1'), '1 node', el(document, 'i', 'n3'), '3 nodes');
  const duo = el(document, 'div', 'bench-duo');
  duo.append(...[latencyPanel(document, model, 'random-read', 'read', 'Read latency'),
    latencyPanel(document, model, 'search', 'search', 'Search latency')].filter(Boolean));
  const parts = selection.type === 'all' ? [overview(document, model, selection.sort), duo, cost(document, model)]
    : [overview(document, model, selection.sort, selection.type)];
  container.replaceChildren(legend, ...parts.filter(Boolean), footer(document, catalog, model, selection.type));
}
