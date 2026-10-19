import { metrics } from './measurements.mjs';
import { ISOLATED, artifactUrl, runUrl } from './isolated-contracts.mjs';

const STATUS = Object.freeze({ measured: 'Measured', unsupported: 'Unsupported scenario',
  unsupportedTopology: 'Unsupported execution topology', failed: 'Benchmark failed · no data' });
const HEADINGS = Object.freeze(['Engine', 'Execution nodes', 'Status', 'Selected metric', 'Attempts', 'Successes', 'Failures', 'GitHub run']);
const FORMAT = new Intl.NumberFormat('en', { maximumFractionDigits: 3 });

function element(document, tag, text) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  return node;
}

function link(document, text, href) {
  const node = element(document, 'a', text);
  node.href = href;
  node.rel = 'noopener noreferrer';
  return node;
}

const display = (value, metric) => Number.isFinite(value) ? FORMAT.format(value) + ' ' + metric.unit : 'Unavailable';

function renderChart(document, rows, metric) {
  const list = element(document, 'ul');
  list.className = 'isolated-chart';
  list.setAttribute('aria-label', metric.title + ' by engine at the selected execution node count');
  const maximum = Math.max(1, ...rows.map(row => row.value).filter(Number.isFinite));
  for (const row of rows) {
    const item = element(document, 'li', row.name + ' · ' + display(row.value, metric));
    if (Number.isFinite(row.value)) {
      const progress = element(document, 'progress');
      progress.max = maximum;
      progress.value = row.value;
      progress.setAttribute('aria-label', row.name + ' · ' + display(row.value, metric));
      item.append(progress);
    }
    list.append(item);
  }
  return list;
}

function renderEvidence(document, worker, cohort) {
  const cell = element(document, 'td');
  cell.append(link(document, worker.job.conclusion === 'failure' ? 'Failed benchmark job' : 'Session worker job', worker.job.url), element(document, 'br'),
    link(document, 'GitHub artifact', artifactUrl(cohort, worker.artifact)), element(document, 'br'),
    element(document, 'small', 'GitHub download/access required; subject to artifact retention.'));
  return cell;
}

function renderTable(document, rows, metric, cohort) {
  const table = element(document, 'table');
  table.append(element(document, 'caption', metric.title + ' · ' + metric.direction + ' · one execution node count'));
  const head = element(document, 'thead');
  const header = element(document, 'tr');
  for (const heading of HEADINGS) {
    const cell = element(document, 'th', heading);
    cell.scope = 'col';
    header.append(cell);
  }
  head.append(header);
  const body = element(document, 'tbody');
  for (const row of rows) {
    const tr = element(document, 'tr');
    tr.dataset.workerId = row.worker.id;
    tr.dataset.value = row.value === null ? '' : String(row.value);
    for (const value of [row.name, row.nodeCount, STATUS[row.status], display(row.value, metric), row.attempts, row.successes, row.failures]) {
      tr.append(element(document, 'td', value === null ? 'Unavailable' : String(value)));
    }
    tr.append(renderEvidence(document, row.worker, cohort));
    body.append(tr);
  }
  table.append(head, body);
  const wrapper = element(document, 'div');
  wrapper.className = 'table-scroll isolated-table-scroll';
  wrapper.tabIndex = 0;
  wrapper.setAttribute('role', 'region');
  wrapper.setAttribute('aria-label', metric.title + ' results table; scroll this region horizontally on narrow screens');
  wrapper.append(table);
  return wrapper;
}

function renderWorker(document, worker) {
  const details = element(document, 'details');
  details.className = 'isolated-worker';
  details.dataset.workerId = worker.id;
  details.append(element(document, 'summary', worker.target + ' · ' + worker.nodeCount + ' execution nodes · worker provenance'));
  const fields = [['Worker', worker.id], ['Raw envelope SHA256', worker.rawSha256], ['Artifact ID', worker.artifact.id],
    ['Artifact name', worker.artifact.name], ['Artifact digest', worker.artifact.digest], ['Session outcome', worker.session.outcome]];
  if (worker.session.reason !== null) fields.push(['Session reason', worker.session.reason]);
  if (worker.reason !== null) fields.push(['Workload result detail', worker.reason]);
  if (worker.report === null) fields.push([worker.disposition === ISOLATED.failed ? 'Benchmark result' : 'Topology limitation', worker.reason]);
  else {
    const report = worker.report;
    const target = report.targets[0];
    fields.push(['Host OS', report.hostOs], ['Architecture', report.architecture], ['Logical processors', report.logicalProcessors],
      ['Runtime', report.runtime], ['Storage', report.storage],
      ['Workload client PID', report.client.processId], ['Workload client DLL SHA-256', report.client.binary.assemblySha256],
      ['Workload client PDB SHA-256', report.client.binary.pdbSha256],
      ['Server version', target.version], ['Execution topology', target.topology],
      ['Data copies', target.cluster.dataCopies], ['Write acknowledgement', target.writeAcknowledgement],
      ['Read contract', target.readContract], ['Transport', target.transport], ['Authorization', target.authorization],
      ['Execution observations', target.cluster.observations.join('; ')]);
    if (report.serverProcesses.length > 0) fields.push(['KeyLoad server processes', report.serverProcesses.map(process =>
      process.name + ': PID ' + process.processId + ', DLL ' + process.binary.assemblySha256
      + ', PDB ' + process.binary.pdbSha256).join('; ')]);
    if (report.serverImage !== null) fields.push(['External database image', report.serverImage]);
  }
  const list = element(document, 'dl');
  for (const [name, value] of fields) list.append(element(document, 'dt', name), element(document, 'dd', String(value)));
  details.append(list);
  return details;
}

export function renderIsolatedView(container, catalog, projection, rows, selection) {
  const document = container.ownerDocument;
  const metric = metrics[selection.metric];
  const fragment = document.createDocumentFragment();
  fragment.append(element(document, 'h3', metric.title + ' · ' + selection.nodeCount + ' execution nodes'),
    element(document, 'p', selection.scenario + ' · ' + (selection.repetition === 'session' ? 'Original execution test result from the shared session' :
      'Unrecognized session selection') + ' · ' + metric.direction),
    element(document, 'p', 'Derived compact projection; raw samples remain in the linked GitHub artifacts. Process CPU, allocation and peak RAM are observed over the selected lane interval, including concurrent lanes; they are not exclusive operation costs and must not be summed. Server resource observations cover the session lifetime.'),
    element(document, 'p', `Profile ${projection.profile}; operations ${projection.options.operations}; concurrency ${projection.options.concurrency}; ` +
      `payload ${projection.options.payloadBytes} bytes; ${projection.datasetSha256 === null ? 'corpus unavailable' : 'corpus SHA256 ' + projection.datasetSha256}.`),
    link(document, 'Measured source ' + catalog.measuredSourceRevision, `https://github.com/${catalog.cohort.repository}/tree/${catalog.measuredSourceRevision}`),
    element(document, 'br'), link(document, 'Website source ' + catalog.siteSourceRevision,
      `https://github.com/${catalog.cohort.repository}/tree/${catalog.siteSourceRevision}`), element(document, 'br'),
    link(document, 'Isolated cohort run · attempt ' + catalog.cohort.attempt, runUrl(catalog.cohort)));
  fragment.append(renderChart(document, rows, metric), renderTable(document, rows, metric, catalog.cohort));
  for (const row of rows) fragment.append(renderWorker(document, row.worker));
  container.replaceChildren(fragment);
}
