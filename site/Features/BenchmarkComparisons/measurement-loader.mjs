import { CONFIG, TEXT } from './contracts.mjs';
import { metrics, scenarios } from './measurements.mjs';

const FIELDS = Object.freeze({ schema: 'schemaVersion', generatedAt: 'generatedAt', siteRevision: 'siteSourceRevision',
  siteKind: 'siteSourceKind', measuredRevision: 'measuredSourceRevision', evidence: 'evidenceUrl', runs: 'runs',
  id: 'id', label: 'label', report: 'report', startedAt: 'startedAt', sourceRevision: 'sourceRevision', sha256: 'sha256',
  runId: 'runId', options: 'options', datasetHash: 'datasetSha256', loadModel: 'loadModel', hostOs: 'hostOs',
  architecture: 'architecture', processors: 'logicalProcessors', runtime: 'runtime', storage: 'storage', targets: 'targets',
  name: 'name', version: 'version', topology: 'topology', write: 'writeAcknowledgement', reads: 'readContract',
  transport: 'transport', authorization: 'authorization', image: 'image', cases: 'cases', target: 'target', scenario: 'scenario',
  repetition: 'repetition', status: 'status', detail: 'detail', measurement: 'measurement', samples: 'samples',
  attempts: 'attempts', successes: 'successes', failures: 'failures', elapsed: 'elapsedSeconds', throughput: 'usefulOperationsPerSecond',
  latency: 'latency', p50: 'p50Ms', p95: 'p95Ms', p99: 'p99Ms', uniqueMessages: 'uniqueCompletedMessages',
  enqueue: 'enqueue', receive: 'receive', ack: 'ack', resources: 'clientResources', cpu: 'cpuSeconds', allocated: 'allocatedBytes',
  peakRss: 'peakObservedWorkingSetBytes', sampleInterval: 'samplingIntervalMs', operation: 'operation', worker: 'worker',
  started: 'startedMs', completed: 'completedMs', success: 'success', error: 'error', payloadBytes: 'payloadBytes',
  completedMessageId: 'completedMessageId', queue: 'queue', latencyMs: 'latencyMs', enqueueMs: 'enqueueMs',
  receiveMs: 'receiveMs', ackMs: 'ackMs', seed: 'seed', documents: 'documents', operations: 'operations', warmup: 'warmup',
  repetitions: 'repetitions', concurrency: 'concurrency', dimensions: 'dimensions', topK: 'topK', timeout: 'timeoutSeconds',
  graphVertices: 'graphVertices', graphFanOut: 'graphFanOut', graphDepth: 'graphDepth' });

const SITE_KIND = Object.freeze({ committed: 'committed_source', preview: 'local_preview' });
const CASE_STATUS = Object.freeze({ measured: 'measured', unsupported: 'unsupported', failed: 'failed' });
const SCENARIO_NAMES = Object.freeze(Object.fromEntries(Object.keys(scenarios).map(name => [name, name])));
const EXPECTED = Object.freeze({
  catalogKeys: Object.freeze([FIELDS.schema, FIELDS.generatedAt, FIELDS.siteRevision, FIELDS.siteKind,
    FIELDS.measuredRevision, FIELDS.evidence, FIELDS.runs]),
  runKeys: Object.freeze([FIELDS.id, FIELDS.label, FIELDS.report, FIELDS.evidence, FIELDS.startedAt,
    FIELDS.sourceRevision, FIELDS.sha256]),
  reportKeys: Object.freeze([FIELDS.schema, FIELDS.runId, FIELDS.startedAt, FIELDS.options, FIELDS.datasetHash,
    FIELDS.loadModel, FIELDS.hostOs, FIELDS.architecture, FIELDS.processors, FIELDS.runtime, FIELDS.storage,
    FIELDS.sourceRevision, FIELDS.targets, FIELDS.cases]),
  targetKeys: Object.freeze([FIELDS.name, FIELDS.version, FIELDS.topology, FIELDS.write, FIELDS.reads,
    FIELDS.transport, FIELDS.authorization, FIELDS.image]),
  caseKeys: Object.freeze([FIELDS.target, FIELDS.scenario, FIELDS.repetition, FIELDS.status, FIELDS.detail,
    FIELDS.measurement, FIELDS.samples]),
  measurementKeys: Object.freeze([FIELDS.attempts, FIELDS.successes, FIELDS.failures, FIELDS.elapsed, FIELDS.throughput,
    FIELDS.latency, FIELDS.uniqueMessages, FIELDS.enqueue, FIELDS.receive, FIELDS.ack, FIELDS.resources]),
  latencyKeys: Object.freeze([FIELDS.p50, FIELDS.p95, FIELDS.p99]),
  resourceKeys: Object.freeze([FIELDS.cpu, FIELDS.allocated, FIELDS.peakRss, FIELDS.sampleInterval]),
  sampleKeys: Object.freeze([FIELDS.operation, FIELDS.worker, FIELDS.started, FIELDS.completed, FIELDS.success, FIELDS.error,
    FIELDS.payloadBytes, FIELDS.completedMessageId, FIELDS.queue, FIELDS.latencyMs]),
  queueKeys: Object.freeze([FIELDS.enqueueMs, FIELDS.receiveMs, FIELDS.ackMs]),
  optionKeys: Object.freeze([FIELDS.seed, FIELDS.documents, FIELDS.operations, FIELDS.warmup, FIELDS.repetitions,
    FIELDS.concurrency, FIELDS.payloadBytes, FIELDS.dimensions, FIELDS.topK, FIELDS.timeout, FIELDS.graphVertices,
    FIELDS.graphFanOut, FIELDS.graphDepth]),
});

const SUPPORTED_SCENARIOS = Object.freeze({
  KeyLoad: Object.freeze(Object.keys(scenarios)),
  'PostgreSQL + pgvector': Object.freeze(Object.keys(scenarios)),
  Qdrant: Object.freeze([SCENARIO_NAMES.VectorExact]),
  RabbitMQ: Object.freeze([SCENARIO_NAMES.QueueCycle]),
  Redis: Object.freeze([SCENARIO_NAMES.PointRead, SCENARIO_NAMES.DocumentWrite]),
  Neo4j: Object.freeze([SCENARIO_NAMES.PointRead, SCENARIO_NAMES.DocumentWrite,
    SCENARIO_NAMES.GraphNeighbors, SCENARIO_NAMES.GraphTraverse]),
});

const LIMITS = Object.freeze({ maximumOption: 1_000_000, maximumRepetitions: 20, maximumPayloadBytes: CONFIG.maximumReportBytes,
  maximumProcessors: 4096, maximumRuntimeSeconds: 86400, maximumSamples: 1_000_000 });
const MESSAGES = Object.freeze({ invalidCatalog: TEXT.invalidCatalog, invalidReport: TEXT.invalidReport,
  badResponse: TEXT.fetchFailure, tooLarge: TEXT.tooLarge,
  unsupportedSchema: TEXT.invalidSchema, aborted: 'The evidence request was cancelled.' });
const NUMBER = Object.freeze({ zero: 0, one: 1, two: 2, percent50: 0.5, percent95: 0.95, percent99: 0.99,
  tolerance: 0.000001, relativeTolerance: 0.000000001 });
const DELIMITERS = Object.freeze({ tuple: '\u0000', empty: '' });
const JSON_ENCODING = Object.freeze({ utf8: CONFIG.utf8, fatal: true });
const JS_TYPES = Object.freeze({ object: 'object', string: 'string', number: 'number' });
const FETCH = Object.freeze({ redirectError: 'error', contentLength: 'content-length', abortName: 'AbortError' });
const HASH_FORMAT = Object.freeze({ radix: 16, padding: 2, leadingZero: '0' });

function assert(condition, message) {
  if (!condition) throw new TypeError(message);
}

function isRecord(value) {
  return value !== null && typeof value === JS_TYPES.object && !Array.isArray(value);
}

function hasExactKeys(value, keys) {
  if (!isRecord(value)) return false;
  const actual = Object.keys(value).sort();
  return actual.length === keys.length && actual.every((key, index) => key === [...keys].sort()[index]);
}

function isNonEmpty(value) {
  return typeof value === JS_TYPES.string && value.trim().length > NUMBER.zero;
}

function isIsoDate(value) {
  return typeof value === JS_TYPES.string && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)$/.test(value)
    && Number.isFinite(Date.parse(value));
}

function isRevision(value) {
  return typeof value === JS_TYPES.string && CONFIG.revisionPattern.test(value);
}

function validateCatalog(value) {
  assert(hasExactKeys(value, EXPECTED.catalogKeys), MESSAGES.invalidCatalog);
  assert(value[FIELDS.schema] === CONFIG.catalogSchemaVersion && isIsoDate(value[FIELDS.generatedAt]), MESSAGES.invalidCatalog);
  assert(isRevision(value[FIELDS.measuredRevision]) && CONFIG.evidencePattern.test(value[FIELDS.evidence]), MESSAGES.invalidCatalog);
  const kind = value[FIELDS.siteKind];
  const siteRevision = value[FIELDS.siteRevision];
  assert((kind === SITE_KIND.preview && siteRevision === null) || (kind === SITE_KIND.committed && isRevision(siteRevision)),
    MESSAGES.invalidCatalog);
  validateCatalogRuns(value);
  return value;
}

function validateCatalogRuns(catalog) {
  const runs = catalog[FIELDS.runs];
  assert(Array.isArray(runs) && runs.length > NUMBER.zero, MESSAGES.invalidCatalog);
  const ids = new Set();
  for (const run of runs) {
    assert(hasExactKeys(run, EXPECTED.runKeys) && CONFIG.idPattern.test(run[FIELDS.id]) && !ids.has(run[FIELDS.id]),
      MESSAGES.invalidCatalog);
    ids.add(run[FIELDS.id]);
    const expectedPath = `runs/${run[FIELDS.id]}/${CONFIG.files.json}`;
    assert(isNonEmpty(run[FIELDS.label]) && run[FIELDS.report] === expectedPath, MESSAGES.invalidCatalog);
    assert(run[FIELDS.evidence] === catalog[FIELDS.evidence] && run[FIELDS.sourceRevision] === catalog[FIELDS.measuredRevision],
      MESSAGES.invalidCatalog);
    assert(isIsoDate(run[FIELDS.startedAt]) && CONFIG.hashPattern.test(run[FIELDS.sha256]), MESSAGES.invalidCatalog);
  }
}

function validateReport(value, expectedRevision) {
  assert(hasExactKeys(value, EXPECTED.reportKeys), MESSAGES.invalidReport);
  assert(value[FIELDS.schema] === CONFIG.schemaVersion, MESSAGES.unsupportedSchema);
  assert(isRevision(expectedRevision) && value[FIELDS.sourceRevision] === expectedRevision, TEXT.invalidRevision);
  validateReportMetadata(value);
  validateOptions(value[FIELDS.options]);
  validateTargets(value[FIELDS.targets]);
  validateCases(value);
  return value;
}

function validateReportMetadata(report) {
  assert(isNonEmpty(report[FIELDS.runId]) && isIsoDate(report[FIELDS.startedAt]), MESSAGES.invalidReport);
  assert(CONFIG.hashPattern.test(report[FIELDS.datasetHash]) && isRevision(report[FIELDS.sourceRevision]), MESSAGES.invalidReport);
  for (const field of [FIELDS.loadModel, FIELDS.hostOs, FIELDS.architecture, FIELDS.runtime, FIELDS.storage]) {
    assert(isNonEmpty(report[field]), MESSAGES.invalidReport);
  }
  assert(Number.isSafeInteger(report[FIELDS.processors]) && report[FIELDS.processors] > NUMBER.zero &&
    report[FIELDS.processors] <= LIMITS.maximumProcessors, MESSAGES.invalidReport);
}

function validateOptions(options) {
  assert(hasExactKeys(options, EXPECTED.optionKeys), MESSAGES.invalidReport);
  for (const [key, value] of Object.entries(options)) {
    const lowerBound = key === FIELDS.warmup ? NUMBER.zero : NUMBER.one;
    assert(Number.isSafeInteger(value) && value >= lowerBound && value <= LIMITS.maximumOption, MESSAGES.invalidReport);
  }
  assert(options[FIELDS.repetitions] <= LIMITS.maximumRepetitions && options[FIELDS.payloadBytes] <= LIMITS.maximumPayloadBytes &&
    options[FIELDS.timeout] <= LIMITS.maximumRuntimeSeconds, MESSAGES.invalidReport);
}

function validateTargets(targets) {
  assert(Array.isArray(targets) && targets.length === CONFIG.targetCount, MESSAGES.invalidReport);
  for (const [index, target] of targets.entries()) {
    const expectedName = CONFIG.engines[index];
    assert(hasExactKeys(target, EXPECTED.targetKeys) && target[FIELDS.name] === expectedName, MESSAGES.invalidReport);
    for (const field of [FIELDS.version, FIELDS.topology, FIELDS.write, FIELDS.reads, FIELDS.transport, FIELDS.authorization]) {
      assert(isNonEmpty(target[field]), MESSAGES.invalidReport);
    }
    if (index === NUMBER.zero) assert(target[FIELDS.image] === null, MESSAGES.invalidReport);
    else assert(typeof target[FIELDS.image] === JS_TYPES.string && /@sha256:[a-f0-9]{64}$/.test(target[FIELDS.image]),
      MESSAGES.invalidReport);
  }
}

function validateCases(report) {
  const options = report[FIELDS.options];
  const cases = report[FIELDS.cases];
  const expectedCount = CONFIG.targetCount * CONFIG.scenarioCount * options[FIELDS.repetitions];
  assert(Array.isArray(cases) && cases.length === expectedCount && expectedCount <= LIMITS.maximumSamples, MESSAGES.invalidReport);
  const casesByTuple = new Map();
  for (const item of cases) {
    validateCase(item, report[FIELDS.targets], options);
    const key = [item[FIELDS.target], item[FIELDS.scenario], item[FIELDS.repetition]].join(DELIMITERS.tuple);
    assert(!casesByTuple.has(key), MESSAGES.invalidReport);
    casesByTuple.set(key, item);
  }
  for (const target of report[FIELDS.targets]) {
    for (const scenario of Object.keys(scenarios)) {
      for (let repetition = NUMBER.zero; repetition < options[FIELDS.repetitions]; repetition += NUMBER.one) {
        const key = [target[FIELDS.name], scenario, repetition].join(DELIMITERS.tuple);
        assert(casesByTuple.has(key), MESSAGES.invalidReport);
      }
    }
  }
}

function validateCase(item, targets, options) {
  assert(hasExactKeys(item, EXPECTED.caseKeys), MESSAGES.invalidReport);
  const target = targets.find(candidate => candidate[FIELDS.name] === item[FIELDS.target]);
  assert(target && Object.hasOwn(scenarios, item[FIELDS.scenario]), MESSAGES.invalidReport);
  assert(Number.isSafeInteger(item[FIELDS.repetition]) && item[FIELDS.repetition] >= NUMBER.zero &&
    item[FIELDS.repetition] < options[FIELDS.repetitions], MESSAGES.invalidReport);
  const supported = SUPPORTED_SCENARIOS[item[FIELDS.target]].includes(item[FIELDS.scenario]);
  if (!supported) {
    assert(item[FIELDS.status] === CASE_STATUS.unsupported && item[FIELDS.measurement] === null &&
      Array.isArray(item[FIELDS.samples]) && item[FIELDS.samples].length === NUMBER.zero && isNonEmpty(item[FIELDS.detail]),
    MESSAGES.invalidReport);
    return;
  }
  assert(item[FIELDS.status] === CASE_STATUS.measured && item[FIELDS.detail] === null, MESSAGES.invalidReport);
  validateMeasurement(item[FIELDS.measurement], item[FIELDS.samples], options, item[FIELDS.scenario]);
}

function validateMeasurement(measurement, samples, options, scenario) {
  assert(hasExactKeys(measurement, EXPECTED.measurementKeys) && Array.isArray(samples), MESSAGES.invalidReport);
  const attempts = options[FIELDS.operations];
  assert(measurement[FIELDS.attempts] === attempts && measurement[FIELDS.successes] === attempts &&
    measurement[FIELDS.failures] === NUMBER.zero && isFiniteNonNegative(measurement[FIELDS.elapsed]) &&
    measurement[FIELDS.elapsed] > NUMBER.zero && isFiniteNonNegative(measurement[FIELDS.throughput]), MESSAGES.invalidReport);
  assert(closeEnough(measurement[FIELDS.throughput], attempts / measurement[FIELDS.elapsed]), MESSAGES.invalidReport);
  validateLatency(measurement[FIELDS.latency]);
  validateResources(measurement[FIELDS.resources]);
  validateQueueMetrics(measurement, scenario, attempts);
  validateSamples(measurement, samples, options, scenario);
}

function validateLatency(value) {
  assert(hasExactKeys(value, EXPECTED.latencyKeys) && isFiniteNonNegative(value[FIELDS.p50]) &&
    value[FIELDS.p50] <= value[FIELDS.p95] && value[FIELDS.p95] <= value[FIELDS.p99] &&
    isFiniteNonNegative(value[FIELDS.p99]), MESSAGES.invalidReport);
}

function validateResources(value) {
  assert(hasExactKeys(value, EXPECTED.resourceKeys) && isFiniteNonNegative(value[FIELDS.cpu]), MESSAGES.invalidReport);
  assert(Number.isSafeInteger(value[FIELDS.allocated]) && value[FIELDS.allocated] >= NUMBER.zero &&
    Number.isSafeInteger(value[FIELDS.peakRss]) && value[FIELDS.peakRss] >= NUMBER.zero &&
    Number.isSafeInteger(value[FIELDS.sampleInterval]) && value[FIELDS.sampleInterval] > NUMBER.zero, MESSAGES.invalidReport);
}

function validateQueueMetrics(measurement, scenario, attempts) {
  const isQueue = scenario === CONFIG.queueScenario;
  if (!isQueue) {
    assert(measurement[FIELDS.enqueue] === null && measurement[FIELDS.receive] === null && measurement[FIELDS.ack] === null &&
      measurement[FIELDS.uniqueMessages] === NUMBER.zero, MESSAGES.invalidReport);
    return;
  }
  assert(measurement[FIELDS.uniqueMessages] === attempts, MESSAGES.invalidReport);
  for (const field of [FIELDS.enqueue, FIELDS.receive, FIELDS.ack]) validateLatency(measurement[field]);
}

function validateSamples(measurement, samples, options, scenario) {
  const operations = options[FIELDS.operations];
  assert(samples.length === operations && samples.length <= LIMITS.maximumSamples, MESSAGES.invalidReport);
  const seenOperations = new Set();
  const seenMessages = new Set();
  const latencies = [];
  const queueLatencies = { enqueue: [], receive: [], ack: [] };
  for (const sample of samples) {
    validateSample(sample, options, scenario, seenOperations, seenMessages, latencies, queueLatencies);
  }
  assert(seenOperations.size === operations, MESSAGES.invalidReport);
  validatePercentiles(measurement[FIELDS.latency], latencies);
  if (scenario === CONFIG.queueScenario) {
    for (const [field, key] of [[FIELDS.enqueue, 'enqueue'], [FIELDS.receive, 'receive'], [FIELDS.ack, 'ack']]) {
      validatePercentiles(measurement[field], queueLatencies[key]);
    }
    assert(seenMessages.size === measurement[FIELDS.uniqueMessages], MESSAGES.invalidReport);
  }
}

function validateSample(sample, options, scenario, seenOperations, seenMessages, latencies, queueLatencies) {
  assert(hasExactKeys(sample, EXPECTED.sampleKeys) && sample[FIELDS.success] === true && sample[FIELDS.error] === null,
    MESSAGES.invalidReport);
  assert(Number.isSafeInteger(sample[FIELDS.operation]) && sample[FIELDS.operation] >= NUMBER.zero &&
    sample[FIELDS.operation] < options[FIELDS.operations] && !seenOperations.has(sample[FIELDS.operation]), MESSAGES.invalidReport);
  seenOperations.add(sample[FIELDS.operation]);
  assert(Number.isSafeInteger(sample[FIELDS.worker]) && sample[FIELDS.worker] >= NUMBER.zero &&
    sample[FIELDS.worker] < options[FIELDS.concurrency], MESSAGES.invalidReport);
  for (const field of [FIELDS.started, FIELDS.completed, FIELDS.latencyMs]) {
    assert(isFiniteNonNegative(sample[field]), MESSAGES.invalidReport);
  }
  assert(sample[FIELDS.completed] >= sample[FIELDS.started] && closeEnough(sample[FIELDS.latencyMs],
    sample[FIELDS.completed] - sample[FIELDS.started]), MESSAGES.invalidReport);
  assert(sample[FIELDS.payloadBytes] === options[FIELDS.payloadBytes], MESSAGES.invalidReport);
  latencies.push(sample[FIELDS.latencyMs]);
  validateSampleQueue(sample, scenario, seenMessages, queueLatencies);
}

function validateSampleQueue(sample, scenario, seenMessages, queueLatencies) {
  if (scenario !== CONFIG.queueScenario) {
    assert(sample[FIELDS.queue] === null && sample[FIELDS.completedMessageId] === null, MESSAGES.invalidReport);
    return;
  }
  const messageId = sample[FIELDS.completedMessageId];
  const queue = sample[FIELDS.queue];
  assert(isNonEmpty(messageId) && !seenMessages.has(messageId) && hasExactKeys(queue, EXPECTED.queueKeys), MESSAGES.invalidReport);
  seenMessages.add(messageId);
  for (const [field, key] of [[FIELDS.enqueueMs, 'enqueue'], [FIELDS.receiveMs, 'receive'], [FIELDS.ackMs, 'ack']]) {
    assert(isFiniteNonNegative(queue[field]), MESSAGES.invalidReport);
    queueLatencies[key].push(queue[field]);
  }
}

function validatePercentiles(summary, values) {
  const ordered = [...values].sort((left, right) => left - right);
  assert(ordered.length > NUMBER.zero, MESSAGES.invalidReport);
  const expected = [NUMBER.percent50, NUMBER.percent95, NUMBER.percent99].map(percentile =>
    ordered[Math.ceil(percentile * ordered.length) - NUMBER.one]);
  assert(summary[FIELDS.p50] === expected[NUMBER.zero] && summary[FIELDS.p95] === expected[NUMBER.one] &&
    summary[FIELDS.p99] === expected[NUMBER.two], MESSAGES.invalidReport);
}

function isFiniteNonNegative(value) {
  return typeof value === JS_TYPES.number && Number.isFinite(value) && value >= NUMBER.zero;
}

function closeEnough(left, right) {
  return Math.abs(left - right) <= Math.max(NUMBER.tolerance, Math.abs(right) * NUMBER.relativeTolerance);
}

export function sha256(bytes) {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  return globalThis.crypto.subtle.digest(CONFIG.sha256, view).then(digest =>
    [...new Uint8Array(digest)].map(byte => byte.toString(HASH_FORMAT.radix)
      .padStart(HASH_FORMAT.padding, HASH_FORMAT.leadingZero)).join(DELIMITERS.empty));
}

function parseJson(bytes, failureMessage) {
  try {
    return JSON.parse(new TextDecoder(JSON_ENCODING.utf8, { fatal: JSON_ENCODING.fatal }).decode(bytes));
  } catch {
    throw new TypeError(failureMessage);
  }
}

async function readResponseBytes(response, maximumBytes, signal) {
  assert(response.ok && response.body, MESSAGES.badResponse);
  const declared = Number(response.headers.get(FETCH.contentLength));
  assert(!Number.isFinite(declared) || declared <= maximumBytes, MESSAGES.tooLarge);
  const reader = response.body.getReader();
  const chunks = [];
  let length = NUMBER.zero;
  try {
    while (true) {
      if (signal?.aborted) throw new DOMException(MESSAGES.aborted, FETCH.abortName);
      const { done, value } = await reader.read();
      if (done) break;
      length += value.byteLength;
      assert(length <= maximumBytes, MESSAGES.tooLarge);
      chunks.push(value);
    }
  } catch (error) {
    await reader.cancel().catch(() => {});
    throw error;
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(length);
  let offset = NUMBER.zero;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return bytes;
}

function resolveCatalogUrl(catalogUrl) {
  const base = globalThis.document?.baseURI ?? globalThis.location?.href;
  assert(typeof base === 'string', TEXT.invalidPath);
  const url = new URL(catalogUrl, base);
  const current = new URL(base);
  assert(url.origin === current.origin && !url.username && !url.password && !url.search && !url.hash, TEXT.invalidPath);
  return url;
}

export async function loadCatalog({ catalogUrl, signal }) {
  const url = resolveCatalogUrl(catalogUrl);
  const response = await fetch(url, { cache: CONFIG.cache, redirect: FETCH.redirectError, signal });
  const bytes = await readResponseBytes(response, CONFIG.maximumCatalogBytes, signal);
  return validateCatalog(parseJson(bytes, MESSAGES.invalidCatalog));
}

function resolveReportUrl(entry, baseUrl) {
  assert(CONFIG.reportPattern.test(entry[FIELDS.report]), TEXT.invalidPath);
  const base = new URL(baseUrl);
  const pageUrl = globalThis.document?.baseURI ?? globalThis.location?.href;
  if (pageUrl) assert(base.origin === new URL(pageUrl).origin, TEXT.invalidPath);
  assert(base.pathname.endsWith('/') && !base.username && !base.password && !base.search && !base.hash, TEXT.invalidPath);
  const url = new URL(entry[FIELDS.report], base);
  assert(url.origin === base.origin && url.pathname.startsWith(base.pathname) && !url.username && !url.password &&
    !url.search && !url.hash, TEXT.invalidPath);
  return url;
}

export async function loadReport({ entry, baseUrl, signal }) {
  const url = resolveReportUrl(entry, baseUrl);
  const response = await fetch(url, { cache: CONFIG.cache, redirect: FETCH.redirectError, signal });
  const bytes = await readResponseBytes(response, CONFIG.maximumReportBytes, signal);
  assert(await sha256(bytes) === entry[FIELDS.sha256], TEXT.invalidHash);
  return validateReport(parseJson(bytes, MESSAGES.invalidReport), entry[FIELDS.sourceRevision]);
}

export { validateCatalog, validateReport };
