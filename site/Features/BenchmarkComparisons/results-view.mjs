import { KEYLOAD, NODES, TYPES, resultKey } from './results-contract.mjs';

class Markup {
  constructor(html) { this.html = html; }
}
const ENTITIES = Object.freeze({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' });
const escape = value => String(value).replace(/[&<>"']/g, character => ENTITIES[character]);
const present = value => value !== null && value !== undefined && value !== false;
const attributes = values => Object.entries(values ?? {}).filter(([, value]) => present(value))
  .map(([key, value]) => ` ${key}="${escape(value === true ? key : value)}"`).join('');

// Emits well-formed markup: text and attribute values are escaped, nested markup is kept as it is.
function h(name, values, ...children) {
  const body = children.flat(Infinity).filter(present).map(child => child instanceof Markup ? child.html : escape(child)).join('');
  return new Markup(`<${name}${attributes(values)}>${body}</${name}>`);
}
const single = (name, values) => new Markup(`<${name}${attributes(values)} />`);

const INTEGER = new Intl.NumberFormat('en', { maximumFractionDigits: 0 });
const ONE = new Intl.NumberFormat('en', { maximumFractionDigits: 1 });
const TWO = new Intl.NumberFormat('en', { maximumFractionDigits: 2 });
const FINE = new Intl.NumberFormat('en', { maximumFractionDigits: 3 });
const SIGNED = new Intl.NumberFormat('en', { maximumFractionDigits: 0, signDisplay: 'exceptZero' });
const compact = value => value >= 1e6 ? ONE.format(value / 1e6) + 'M' : value >= 1e4 ? ONE.format(value / 1e3) + 'k'
  : (value >= 100 ? INTEGER : TWO).format(value);
const change = (one, three) => SIGNED.format((three - one) / one * 100).replace('-', '−') + '%';
const seconds = value => (value >= 100 ? INTEGER : value >= 10 ? ONE : TWO).format(value) + ' s';
const percent = ratio => (Math.min(1, Math.max(0, ratio)) * 100).toFixed(2) + '%';
const nodesLabel = count => count + (count === 1 ? ' node' : ' nodes');
const unique = values => [...new Set(values)];
const firstLine = error => {
  const line = error.split(/\r?\n/)[0].trim();
  return line.length > 90 ? line.slice(0, 89) + '…' : line;
};
const runUrl = runId => `https://github.com/managedcode/KeyLoad/actions/runs/${runId}`;
const link = (text, href, extra) => h('a', { href, rel: 'noopener noreferrer', ...extra }, text);

function typeModel(type, all) {
  const results = all.filter(result => result.type === type.id);
  const byKey = new Map(results.map(result => [resultKey(result), result]));
  const result = (database, nodes) => byKey.get(`${type.id}|${database}|${nodes}`);
  // A case the job never reached carries the job's own error when the job failed before it.
  const line = (database, item, nodes) => {
    const recorded = result(database, nodes);
    const entry = recorded?.cases.find(candidate => candidate.id === item.id);
    if (recorded === undefined) return { nodes, status: 'missing', reason: 'no result' };
    if (entry === undefined && recorded.error !== null) return { nodes, status: 'failed', reason: firstLine(recorded.error), error: recorded.error };
    if (entry === undefined) return { nodes, status: 'missing', reason: 'not run' };
    if (entry.status === 'failed') return { nodes, status: 'failed', reason: firstLine(entry.error), error: entry.error, entry };
    return { nodes, status: 'passed', value: item.seconds ? entry.seconds : entry.opsPerSecond, entry };
  };
  // A database without a 3-node result shows one line per case and says "Single node" once beside its name.
  const nodes = database => NODES.filter(count => count === 1 || result(database, count) !== undefined);
  const best = new Map(type.cases.map(item => {
    const values = type.databases.flatMap(database => NODES.map(count => line(database, item, count)))
      .filter(entry => entry.status === 'passed' && entry.value > 0).map(entry => entry.value);
    return [item.id, values.length === 0 ? 0 : item.seconds ? Math.min(...values) : Math.max(...values)];
  }));
  return { type, results, result, line, nodes, best };
}

function lineTitle(item, line) {
  if (line.status === 'missing') return `${nodesLabel(line.nodes)} · No data: ${line.reason}`;
  if (line.status === 'failed') return `${nodesLabel(line.nodes)} · Failed: ${line.error}`;
  const { entry } = line;
  const batch = entry.batchSize > 1 ? ` · ${INTEGER.format(entry.batchSize)} per call` : '';
  return `${nodesLabel(line.nodes)} · ` + (item.seconds ? `${FINE.format(entry.seconds)} s` : `${INTEGER.format(entry.opsPerSecond)} ops/s · ` +
    `p50 ${TWO.format(entry.p50Ms)} ms · p95 ${TWO.format(entry.p95Ms)} ms · p99 ${TWO.format(entry.p99Ms)} ms per call${batch}`);
}

// One line per node count: the value and its change above a full-width bar scaled to the type's best result.
// Time cases count a shorter time as faster, so their change and bar compare speed as well.
function pairLine(model, item, line, one, visible) {
  const values = { class: 'pair-line n' + line.nodes, 'data-nodes': line.nodes, 'data-status': line.status,
    'data-value': line.status === 'passed' ? line.value : '', title: lineTitle(item, line) };
  if (line.status !== 'passed') return h('span', values, visible ? [h('small', { class: 'no-data' },
    line.status === 'failed' ? 'Failed' : 'No data'), h('small', { class: 'no-data-reason' }, line.reason)] : null);
  const best = model.best.get(item.id);
  const ratio = !(best > 0 && line.value > 0) ? 0 : item.seconds ? best / line.value : line.value / best;
  const note = line.nodes !== 3 || !(one > 0 && line.value > 0) ? null
    : h('small', null, item.seconds ? change(line.value, one) : change(one, line.value));
  return h('span', values, h('b', null, item.seconds ? seconds(line.value) : compact(line.value)), note,
    h('i', { style: `--size: ${percent(ratio)}` }));
}

// Lines sharing one disposition state it once.
function pair(model, database, item) {
  const lines = model.nodes(database).map(nodes => model.line(database, item, nodes));
  const [first] = lines;
  const one = first.status === 'passed' ? first.value : null;
  const shared = lines.every(line => line.status !== 'passed' && line.status === first.status && line.reason === first.reason);
  return h('span', { class: 'pair' }, lines.map((line, index) => pairLine(model, item, line, one, index === 0 || !shared)));
}

function rowHead(model, database) {
  const recorded = NODES.filter(count => model.result(database, count) !== undefined);
  const note = recorded.length === 0 ? 'No result' : recorded.length === 2 ? null : recorded[0] === 1 ? 'Single node' : '3 nodes only';
  return h('th', { scope: 'row' }, h('span', { class: 'engine-name' }, h('strong', null, database), present(note) ? h('small', null, note) : null));
}

function scrollRegion(className, label, content) {
  return h('div', { class: 'scroll-frame' }, h('div', { class: className, tabindex: '0', role: 'region', 'aria-label': label }, content));
}

function comparison(model) {
  const { type } = model;
  const head = h('tr', { class: 'ov-head' }, h('th', { scope: 'col' }, 'Database'), type.cases.map(item =>
    h('th', { scope: 'col', 'data-case': item.id, 'data-kind': item.kind }, h('span', null, item.label,
      h('small', null, item.seconds ? 'seconds' : 'ops/s')))));
  const rows = type.databases.map(database => h('tr', { class: 'ov-row' + (database === KEYLOAD ? ' is-keyload' : ''),
    'data-database': database }, rowHead(model, database), type.cases.map(item =>
    h('td', { 'data-case': item.id, 'data-kind': item.kind }, pair(model, database, item)))));
  const grid = h('table', { class: 'comparison-grid', style: `--columns: ${type.cases.length}`, role: 'table',
    'aria-label': type.label + ' comparisons' }, h('thead', null, head), h('tbody', { class: 'ov-list' }, rows));
  return scrollRegion('comparison-scroll', `${type.label} comparison table; scroll horizontally to see every case`, grid);
}


const STATUS = Object.freeze({ passed: 'Passed', failed: 'Failed', missing: 'Not run' });
const DETAIL_HEAD = Object.freeze([['Case'], ['Status'], ['Operations', 'num'], ['Errors', 'num'], ['Seconds', 'num'],
  ['ops/s', 'num'], ['p50 ms', 'num'], ['p95 ms', 'num'], ['p99 ms', 'num'], ['Per call', 'num'], ['Error'], ['Image'], ['Run']]);
const FIELDS = Object.freeze([['operations', INTEGER], ['errors', INTEGER], ['seconds', FINE], ['opsPerSecond', TWO],
  ['p50Ms', TWO], ['p95Ms', TWO], ['p99Ms', TWO], ['batchSize', INTEGER]]);

// Every recorded case keeps its exact value in the cell title and data attribute; a failed case keeps its partial values.
// A case the job never reached after a job failure shows that job error.
function detailRow(result, database, nodes, item, entry) {
  const jobFailed = entry === undefined && result !== undefined && result.error !== null;
  const status = entry?.status ?? (jobFailed ? 'failed' : 'missing');
  const number = ([field, format]) => {
    const value = entry?.[field] ?? null;
    return h('td', { class: 'num', 'data-field': field, 'data-value': value ?? '', title: value ?? null },
      value === null ? '—' : format.format(value));
  };
  const run = !(result?.runId > 0) ? '—' : link('Run ' + result.runId, runUrl(result.runId));
  return h('tr', { 'data-database': database, 'data-nodes': nodes, 'data-case': item?.id ?? null, 'data-status': status },
    h('td', { class: 'pin' }, database), h('td', { class: 'pin pin-nodes' }, String(nodes)),
    h('td', null, item?.label ?? '—'), h('td', null, result === undefined ? 'No result' : STATUS[status]), FIELDS.map(number),
    h('td', { class: 'error-text' }, entry?.error ?? (jobFailed ? result.error : '')), h('td', { class: 'image' }, result?.image || '—'),
    h('td', null, run));
}

function details(model) {
  const { type } = model;
  const rows = type.databases.flatMap(database => {
    const recorded = NODES.filter(count => model.result(database, count) !== undefined);
    if (recorded.length === 0) return [detailRow(undefined, database, 1, undefined, undefined)];
    return recorded.flatMap(nodes => type.cases.map(item => detailRow(model.result(database, nodes), database, nodes, item,
      model.result(database, nodes).cases.find(candidate => candidate.id === item.id))));
  });
  const head = h('tr', null, h('th', { class: 'pin', scope: 'col' }, 'Database'), h('th', { class: 'pin pin-nodes', scope: 'col' }, 'Nodes'),
    DETAIL_HEAD.map(([label, className]) => h('th', { class: className ?? null, scope: 'col' }, label)));
  const table = h('table', null, h('caption', null, h('span', null, `${type.label} · every recorded case per job`)),
    h('thead', null, head), h('tbody', null, rows));
  return h('details', { class: 'bench-details' }, h('summary', null, 'All recorded values'),
    scrollRegion('table-scroll', `${type.label} recorded values; scroll this region horizontally on narrow screens`, table));
}

function subtitle(model) {
  if (model.results.length === 0) return 'No results in this run.';
  const list = values => unique(values).map(value => INTEGER.format(value)).join(' and ');
  return `${list(model.results.map(result => result.records))} records · ${list(model.results.map(result => result.operations))} ` +
    `operations per case · ${list(model.results.map(result => result.clients))} clients. Operations per second (records for loads): ` +
    'top line 1 node, lower line 3 nodes with the change against 1 node.';
}

function typeSection(model) {
  const { type } = model;
  return h('section', { class: 'bench-panel bench-overview', id: 'bench-' + type.id, 'data-type': type.id,
    'data-label': type.label, 'aria-labelledby': `bench-${type.id}-title` },
  h('header', { class: 'bench-panel-head' }, h('h3', { id: `bench-${type.id}-title` }, type.label), h('p', null, subtitle(model))),
  h('p', { class: 'table-scroll-hint' }, 'Swipe or scroll horizontally to see all columns.'), comparison(model), details(model));
}

function kpis(results) {
  const passed = results.flatMap(result => result.cases.filter(item => item.status === 'passed'));
  const databases = unique(results.filter(result => result.cases.some(item => item.status === 'passed')).map(result => result.database));
  return h('div', { class: 'bench-kpis' }, [[String(unique(results.map(result => result.type)).length), 'data types compared'],
    [String(databases.length), 'databases measured'], [compact(passed.reduce((sum, item) => sum + item.operations, 0)), 'operations timed'],
    [String(results.length), 'benchmark jobs']].map(([value, label]) => h('div', { class: 'kpi' }, h('strong', null, value), h('span', null, label))));
}

// The type filter works only with the page script, so it stays hidden until that script shows it.
function controls() {
  const chip = (value, label, count) => h('label', { class: 'chip' },
    single('input', { type: 'radio', name: 'bench-type', value, checked: value === 'all' }), h('span', null, label, h('small', null, String(count))));
  return h('div', { class: 'bench-controls', hidden: true }, h('fieldset', { id: 'bench-type', class: 'bench-chips' },
    h('legend', null, 'Data type'), h('span', { class: 'chip-row' }, chip('all', 'All types', TYPES.length),
      TYPES.map(type => chip(type.id, type.label, type.databases.length)))));
}

function footer(results) {
  const commits = unique(results.map(result => result.commit).filter(Boolean)).map(commit => /^[0-9a-f]{40}$/.test(commit)
    ? link('Source ' + commit.slice(0, 7), 'https://github.com/managedcode/KeyLoad/tree/' + commit) : h('span', null, 'Source ' + commit));
  const runs = unique(results.map(result => result.runId).filter(runId => runId > 0)).map(runId => link('GitHub Actions run ' + runId, runUrl(runId)));
  const instants = results.flatMap(result => [result.startedAt, result.finishedAt]).filter(instant => instant !== null)
    .map(instant => new Date(instant).toISOString().slice(0, 10)).sort();
  const days = unique([instants[0], instants.at(-1)]);
  return h('footer', { class: 'bench-foot' }, h('p', { class: 'bench-meta' }, runs, commits, h('span', null, 'Measured ' + days.join(' to ')),
    link('Download results.json', './data/results.json', { download: 'results.json' })),
  h('p', { class: 'bench-note' }, 'Every job ran alone on its own GitHub-hosted runner, so hardware can differ between engines. ' +
    'Figures describe this run, not production readiness.'));
}

export function renderResults({ results }) {
  const legend = h('p', { class: 'bench-legend' }, h('i', { class: 'n1' }), '1 node', h('i', { class: 'n3' }), '3 nodes');
  return h('div', { class: 'instrument' }, kpis(results), controls(), h('div', { class: 'measurement-workspace' },
    h('div', { id: 'bench-results' }, legend, TYPES.map(type => typeSection(typeModel(type, results))), footer(results)),
    h('p', { id: 'bench-announcement', class: 'sr-only', role: 'status', 'aria-live': 'polite', 'aria-atomic': 'true' }))).html;
}
