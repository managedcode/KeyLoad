import { CONFIG, DOM, IDS, SELECTORS, TEXT } from './contracts.mjs';
import { loadCatalog, loadReport } from './measurement-loader.mjs';
import { colors, metrics, scenarios, selectedRows } from './measurements.mjs';
import { renderChart } from './benchmark-chart.mjs';
import { renderConditions, renderProfiles } from './benchmark-profiles.mjs';

const FIELDS = Object.freeze({ id: 'id', label: 'label', report: 'report', evidence: 'evidenceUrl', startedAt: 'startedAt',
  sourceRevision: 'sourceRevision', sha256: 'sha256', siteKind: 'siteSourceKind', siteRevision: 'siteSourceRevision',
  measuredRevision: 'measuredSourceRevision', datasetHash: 'datasetSha256', hostOs: 'hostOs', architecture: 'architecture',
  processors: 'logicalProcessors', runtime: 'runtime', options: 'options', targets: 'targets', name: 'name',
  version: 'version', topology: 'topology', write: 'writeAcknowledgement', reads: 'readContract', transport: 'transport',
  authorization: 'authorization', image: 'image', scenario: 'scenario', status: 'status', value: 'value', attempts: 'attempts',
  successes: 'successes', failures: 'failures', throughput: 'throughput', p50: 'p50', p95: 'p95', p99: 'p99',
  min: 'min', max: 'max', detail: 'detail', repetitions: 'repetitions', graphDepth: 'graphDepth' });
const KIND = Object.freeze({ preview: 'local_preview' });
const SCENARIO = Object.freeze({ graphTraverse: 'GraphTraverse' });
const DATASET = Object.freeze({ scenario: 'scenario' });
const DISPLAY = Object.freeze({ loaded: 'Published measurements loaded.', loading: 'Loading published measurements…',
  profileLoading: 'Loading this workload profile…', measured: 'Measured', retry: 'Retry published measurements', utc: 'UTC', corpus: ' · corpus ',
  cpus: ' logical CPUs · ', separator: ' · ', slash: ' / ', depth: ' Depth: ', hops: ' hops.', one: 1 });
const TAGS = Object.freeze({ option: 'option', p: 'p', tr: 'tr', td: 'td', span: 'span' });
const ENGINE = Object.freeze({ keyLoad: CONFIG.engines[0] });
const TAB = Object.freeze({ prefix: 'tab-' });
const NUMBER = Object.freeze({ zero: 0, one: 1, two: 2 });
const DOWNLOAD = Object.freeze({ json: 'json' });
const URL_PARTS = Object.freeze({ currentDirectory: '.' });

function element(documentRef, tag, className, text) {
  const node = documentRef.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function createState(root, catalogUrl) {
  const byId = id => root.getElementById(id);
  return {
    root, catalogUrl, byId, catalog: null, report: null, currentEntry: null, scenario: CONFIG.defaultScenario,
    metric: CONFIG.defaultMetric, repetition: CONFIG.median, generation: 0, disposed: false,
    catalogController: null, reportController: null, listeners: [],
    ui: Object.fromEntries(Object.entries(IDS).map(([name, id]) => [name, byId(id)])),
    tabs: [...root.querySelectorAll(SELECTORS.scenario)],
  };
}

function createNumberFormatter() {
  return new Intl.NumberFormat(CONFIG.numberLocale, { maximumFractionDigits: NUMBER.two });
}

function formatNumber(value, formatter, decimals = NUMBER.two) {
  if (value === null || !Number.isFinite(value)) return TEXT.unrecorded;
  return decimals === NUMBER.zero
    ? new Intl.NumberFormat(CONFIG.numberLocale, { maximumFractionDigits: NUMBER.zero }).format(value)
    : formatter.format(value);
}

function listen(state, target, eventName, handler) {
  target.addEventListener(eventName, handler);
  state.listeners.push(() => target.removeEventListener(eventName, handler));
}

function clearDownloads(state) {
  for (const key of Object.keys(CONFIG.files)) {
    const link = state.ui[key];
    link.removeAttribute(DOM.attributes.href);
    link.setAttribute(DOM.attributes.disabled, 'true');
  }
  state.ui.workflow.href = CONFIG.actionsUrl;
  state.ui.workflow.removeAttribute(DOM.attributes.disabled);
}

function clearReportSurfaces(state, message) {
  state.report = null;
  state.ui.panel.setAttribute(DOM.attributes.busy, 'true');
  state.ui.chart.replaceChildren(element(state.root, TAGS.p, DOM.classes.empty, message));
  state.ui.chartTitle.textContent = TEXT.unavailable;
  state.ui.description.textContent = TEXT.unavailable;
  state.ui.direction.textContent = '';
  state.ui.metricNote.textContent = '';
  state.ui.table.replaceChildren();
  state.ui.conditions.replaceChildren();
  state.ui.profiles.replaceChildren(element(state.root, TAGS.p, DOM.classes.empty, message));
  state.ui.publishedDate.textContent = TEXT.unavailable;
  state.ui.host.textContent = '';
  state.ui.revision.textContent = '';
  state.ui.siteRevision.textContent = '';
  state.ui.engineCount.textContent = TEXT.unrecorded;
  state.ui.error.hidden = true;
  state.ui.retry.hidden = true;
  state.ui.announcement.textContent = message;
  clearDownloads(state);
}

function setError(state, message) {
  clearReportSurfaces(state, TEXT.unavailable);
  state.ui.error.textContent = message;
  state.ui.error.hidden = false;
  state.ui.retry.hidden = false;
  state.ui.retry.textContent = DISPLAY.retry;
  state.ui.panel.setAttribute(DOM.attributes.busy, 'false');
  state.ui.announcement.textContent = message;
}

function updateMetricAvailability(state) {
  if (metrics[state.metric].queue && state.scenario !== CONFIG.queueScenario) {
    state.metric = CONFIG.defaultMetric;
    state.ui.metric.value = state.metric;
  }
  for (const option of state.ui.metric.options) {
    option.disabled = Boolean(metrics[option.value]?.queue && state.scenario !== CONFIG.queueScenario);
  }
}

function selectScenario(state, scenario, focus = false) {
  state.scenario = scenario;
  updateMetricAvailability(state);
  syncScenarioTabs(state, focus);
  if (state.report) renderReport(state);
}

function syncScenarioTabs(state, focus = false) {
  for (const tab of state.tabs) {
    const active = tab.dataset[DATASET.scenario] === state.scenario;
    tab.setAttribute(DOM.attributes.selected, String(active));
    tab.tabIndex = active ? NUMBER.zero : -NUMBER.one;
    if (active && focus) tab.focus();
  }
  state.ui.panel.setAttribute(DOM.attributes.labelledBy, `${TAB.prefix}${state.scenario}`);
}

function bindScenarios(state) {
  for (const [index, tab] of state.tabs.entries()) {
    const select = () => selectScenario(state, tab.dataset[DATASET.scenario]);
    listen(state, tab, DOM.events.click, select);
    listen(state, tab, DOM.events.keydown, event => {
      const next = nextTabIndex(event.key, index, state.tabs.length);
      if (next === null) return;
      event.preventDefault();
      selectScenario(state, state.tabs[next].dataset[DATASET.scenario], true);
    });
  }
}

function nextTabIndex(key, current, count) {
  if (key === DOM.keys.right) return (current + DISPLAY.one) % count;
  if (key === DOM.keys.left) return (current - DISPLAY.one + count) % count;
  if (key === DOM.keys.home) return NUMBER.zero;
  if (key === DOM.keys.end) return count - DISPLAY.one;
  return null;
}

function bindControls(state) {
  listen(state, state.ui.profile, DOM.events.change, () => {
    const entry = state.catalog.runs.find(run => run[FIELDS.id] === state.ui.profile.value);
    if (entry) void loadSelectedReport(state, entry);
  });
  listen(state, state.ui.metric, DOM.events.change, () => { state.metric = state.ui.metric.value; renderReport(state); });
  listen(state, state.ui.repetition, DOM.events.change, () => { state.repetition = state.ui.repetition.value; renderReport(state); });
  listen(state, state.ui.logScale, DOM.events.change, () => renderReport(state));
  listen(state, state.ui.retry, DOM.events.click, () => state.catalog
    ? void loadSelectedReport(state, state.currentEntry) : void loadCatalogAndReport(state));
  bindScenarios(state);
}

function addProfileOptions(state, catalog) {
  const documentRef = state.root;
  const options = catalog.runs.map(entry => {
    const option = element(documentRef, TAGS.option, '', entry[FIELDS.label]);
    option.value = entry[FIELDS.id];
    return option;
  });
  state.ui.profile.replaceChildren(...options);
  state.ui.profile.disabled = false;
}

function cancelReportRequest(state) {
  state.reportController?.abort();
  state.reportController = new AbortController();
  state.generation += DISPLAY.one;
  return state.generation;
}

async function loadCatalogAndReport(state) {
  const token = cancelReportRequest(state);
  state.catalogController?.abort();
  state.catalogController = new AbortController();
  state.currentEntry = null;
  clearReportSurfaces(state, DISPLAY.loading);
  try {
    const catalog = await loadCatalog({ catalogUrl: state.catalogUrl, signal: state.catalogController.signal });
    if (!isCurrent(state, token)) return;
    state.catalog = catalog;
    addProfileOptions(state, catalog);
    await loadSelectedReport(state, catalog.runs[NUMBER.zero]);
  } catch {
    if (isCurrent(state, token)) setError(state, TEXT.loadFailure);
  }
}

async function loadSelectedReport(state, entry) {
  if (!entry || state.disposed) return;
  const token = cancelReportRequest(state);
  state.currentEntry = entry;
  state.repetition = CONFIG.median;
  state.ui.repetition.replaceChildren(createOption(state.root, TEXT.median, CONFIG.median));
  clearReportSurfaces(state, DISPLAY.profileLoading);
  state.ui.profile.value = entry[FIELDS.id];
  try {
    const reportUrl = new URL(state.catalogUrl, state.root.baseURI);
    const baseUrl = new URL(URL_PARTS.currentDirectory, reportUrl);
    const report = await loadReport({ entry, baseUrl, signal: state.reportController.signal });
    if (!isCurrent(state, token)) return;
    state.report = report;
    addRepetitionOptions(state, report[FIELDS.options]);
    attachDownloads(state, entry, baseUrl);
    renderProvenance(state, entry, report);
    renderProfiles(state.ui.profiles, report[FIELDS.targets]);
    state.ui.engineCount.textContent = String(report[FIELDS.targets].length);
    state.ui.error.hidden = true;
    state.ui.retry.hidden = true;
    state.ui.panel.setAttribute(DOM.attributes.busy, 'false');
    state.ui.announcement.textContent = DISPLAY.loaded;
    renderReport(state);
  } catch {
    if (isCurrent(state, token)) setError(state, TEXT.loadFailure);
  }
}

function createOption(documentRef, label, value) {
  const option = element(documentRef, TAGS.option, '', label);
  option.value = value;
  return option;
}

function addRepetitionOptions(state, options) {
  const repetitions = Array.from({ length: options[FIELDS.repetitions] }, (_, index) =>
    createOption(state.root, TEXT.repetition(index), String(index)));
  state.ui.repetition.append(...repetitions);
}

function attachDownloads(state, entry, baseUrl) {
  const reportUrl = new URL(entry[FIELDS.report], baseUrl);
  const directory = new URL('.', reportUrl);
  for (const [key, file] of Object.entries(CONFIG.files)) {
    const link = state.ui[key];
    link.href = key === DOWNLOAD.json ? reportUrl.href : new URL(file, directory).href;
    link.download = file;
    link.removeAttribute(DOM.attributes.disabled);
  }
  state.ui.workflow.href = entry[FIELDS.evidence];
  state.ui.workflow.removeAttribute(DOM.attributes.disabled);
}

function renderProvenance(state, entry, report) {
  const formatter = new Intl.DateTimeFormat(CONFIG.numberLocale, {
    dateStyle: 'medium', timeStyle: 'short', timeZone: CONFIG.dateTimeZone,
  });
  state.ui.publishedDate.textContent = `${formatter.format(new Date(entry[FIELDS.startedAt]))} ${DISPLAY.utc}`;
  state.ui.host.textContent = `${report[FIELDS.hostOs]}${DISPLAY.separator}${report[FIELDS.architecture]}${DISPLAY.separator}` +
    `${report[FIELDS.processors]}${DISPLAY.cpus}${report[FIELDS.runtime]}`;
  state.ui.revision.textContent = `${TEXT.source(report[FIELDS.sourceRevision])}${DISPLAY.corpus}${report[FIELDS.datasetHash].slice(0, 12)}`;
  state.ui.siteRevision.textContent = state.catalog[FIELDS.siteKind] === KIND.preview
    ? TEXT.localPreview : TEXT.siteSource(state.catalog[FIELDS.siteRevision]);
}

function isCurrent(state, token) {
  return !state.disposed && token === state.generation;
}

function renderReport(state) {
  if (!state.report || state.disposed) return;
  updateMetricAvailability(state);
  const scenarioDetails = scenarios[state.scenario];
  state.ui.chartTitle.textContent = metrics[state.metric].title;
  state.ui.direction.textContent = metrics[state.metric].direction;
  state.ui.description.textContent = scenarioDetails[NUMBER.one] +
    (state.scenario === SCENARIO.graphTraverse
      ? `${DISPLAY.depth}${state.report.options[FIELDS.graphDepth]}${DISPLAY.hops}` : '');
  syncScenarioTabs(state);
  const rows = selectedRows(state.report, state.scenario, state.repetition, state.metric);
  renderChart({ chart: state.ui.chart, title: state.ui.chartTitle, direction: state.ui.direction, note: state.ui.metricNote,
    rows, metricName: state.metric, repetition: state.repetition, logarithmic: state.ui.logScale.checked });
  renderConditions(state.ui.conditions, state.report.options, state.scenario);
  renderTable(state, rows);
}

function renderTable(state, rows) {
  const formatter = createNumberFormatter();
  const rendered = rows.map(row => createTableRow(state, row, formatter));
  state.ui.table.replaceChildren(...rendered);
}

function createTableRow(state, row, formatter) {
  const documentRef = state.root;
  const rowClass = row[FIELDS.name] === ENGINE.keyLoad ? DOM.classes.keyloadTable : '';
  const tr = element(documentRef, TAGS.tr, rowClass);
  const name = element(documentRef, TAGS.td);
  const label = element(documentRef, TAGS.span, DOM.classes.cellEngine, row[FIELDS.name]);
  label.prepend(createDot(documentRef, row[FIELDS.name]));
  name.append(label);
  tr.append(name);
  for (const key of [FIELDS.throughput, FIELDS.p50, FIELDS.p95, FIELDS.p99]) {
    const value = row[key];
    tr.append(element(documentRef, TAGS.td, '', formatNumber(value, formatter)));
  }
  tr.append(element(documentRef, TAGS.td, '', row[FIELDS.attempts]
    ? `${formatNumber(row[FIELDS.successes], formatter, NUMBER.zero)}${DISPLAY.slash}${formatNumber(row[FIELDS.attempts], formatter, NUMBER.zero)}`
    : TEXT.unrecorded));
  const statusCell = element(documentRef, TAGS.td);
  const statusText = row[FIELDS.status] === CONFIG.measured ? DISPLAY.measured : row[FIELDS.status] === CONFIG.failed ? TEXT.failed : TEXT.unsupported;
  statusCell.append(element(documentRef, TAGS.span, `${DOM.classes.status} ${row[FIELDS.status]}`, statusText));
  tr.append(statusCell);
  return tr;
}

function createDot(documentRef, name) {
  const dot = element(documentRef, TAGS.span, DOM.classes.dot);
  dot.style.setProperty(DOM.properties.engine, colors[name]);
  dot.setAttribute(DOM.attributes.hidden, 'true');
  return dot;
}

function dispose(state) {
  if (state.disposed) return;
  state.disposed = true;
  state.generation += DISPLAY.one;
  state.catalogController?.abort();
  state.reportController?.abort();
  for (const remove of state.listeners) remove();
  state.listeners = [];
}

export function mountBenchmarkLab({ root, catalogUrl }) {
  const state = createState(root, catalogUrl);
  bindControls(state);
  selectScenario(state, state.scenario);
  updateMetricAvailability(state);
  clearReportSurfaces(state, DISPLAY.loading);
  void loadCatalogAndReport(state);
  return { dispose: () => dispose(state) };
}
