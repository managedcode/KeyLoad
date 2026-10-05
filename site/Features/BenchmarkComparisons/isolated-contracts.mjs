// Browser wire is checked against the canonical native contract by every projection build.
export const ISOLATED = Object.freeze({
  projectionVersion: 1, catalogVersion: 1, aggregateVersion: 4, reportVersion: 3,
  profile: 'intensive-1k-c16', repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks',
  targets: Object.freeze(['KeyLoad', 'PostgreSQL + pgvector', 'Qdrant', 'RabbitMQ', 'Redis', 'Neo4j', 'MongoDB', 'OpenSearch', 'KurrentDB', 'SurrealDB', 'HelixDB']),
  nodes: Object.freeze([1, 2, 3]),
  crud: Object.freeze(['PointRead', 'DocumentWrite', 'DocumentUpdate', 'DocumentDelete']),
  specialized: Object.freeze(['VectorExact', 'QueueCycle', 'GraphNeighbors', 'GraphTraverse', 'StreamAppend', 'StreamRead']),
  options: Object.freeze({ seed: 1729, documents: 4096, operations: 10000, warmup: 256, repetitions: 5, concurrency: 16,
    payloadBytes: 1024, dimensions: 128, topK: 10, graphVertices: 256, graphFanOut: 3, graphDepth: 3, timeoutSeconds: 30 }),
  unsupportedTopologies: Object.freeze([{ target: 'Neo4j', nodeCounts: [2, 3],
    reason: 'Neo4j Community does not provide native clustering; Enterprise licensing is excluded.' },
    { target: 'SurrealDB', nodeCounts: [2, 3], reason: "SurrealDB Community's experimental TiKV clustering is outside the qualified native topology; only the persistent standalone server is measured." },
    { target: 'HelixDB', nodeCounts: [2, 3], reason: 'The self-hosted HelixDB server is standalone; managed cloud high availability is outside this native comparison.' }]),
  topology: Object.freeze({ 1: 'Single', 2: 'TwoNode', 3: 'Replicated' }),
  steps: Object.freeze(['Run database workload', 'Save benchmark results']),
  catalogBytes: 65_536, projectionBytes: 4_194_304, workers: 330,
  rawLocation: 'githubActionsArtifacts', aggregatePath: 'isolated/aggregate.json', projectionPath: 'isolated/projection.json',
  sha: /^[a-f0-9]{40}$/, hash: /^[a-f0-9]{64}$/, image: /@sha256:[a-f0-9]{64}$/,
  guid: /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i,
  linux: /(?:^|\s|\/)(?:Linux|Ubuntu)(?:\s|$)/i,
  date: /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)$/,
  error: 'E_ISOLATED_EVIDENCE', tolerance: 0.00000001,
  failed: 'failed', failure: 'failure', success: 'success',
  failureReason: 'Benchmark failed; no measurement data is available.',
});

export const WIRE = Object.freeze({
  projection: ['schemaVersion', 'cohort', 'profile', 'options', 'datasetSha256', 'workers'],
  catalog: ['schemaVersion', 'generatedAt', 'siteSourceRevision', 'measuredSourceRevision', 'evidenceUrl',
    'cohort', 'aggregate', 'projection', 'rawLocation'],
  file: ['path', 'sha256'],
  cohort: ['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow', 'profile'],
  metadata: ['id', 'target', 'nodeCount', 'scenario', 'profile', 'disposition', 'reason', 'rawPath', 'rawSha256', 'job', 'artifact'],
  worker: ['id', 'target', 'nodeCount', 'scenario', 'profile', 'disposition', 'reason', 'rawPath', 'rawSha256', 'job', 'artifact', 'report'],
  job: ['id', 'name', 'url', 'conclusion', 'steps'], step: ['name', 'conclusion'],
  artifact: ['id', 'name', 'sizeInBytes', 'digest', 'expired'],
  report: ['schemaVersion', 'runId', 'startedAt', 'options', 'datasetSha256', 'loadModel', 'hostOs', 'architecture',
    'logicalProcessors', 'runtime', 'storage', 'sourceRevision', 'targets', 'cases', 'provenance', 'loadGeneratorImage'],
  provenance: ['runId', 'attempt', 'repository', 'ref', 'workflow', 'profile'],
  target: ['name', 'version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization', 'image', 'cluster'],
  cluster: ['nodes', 'dataCopies', 'state', 'observations'],
  case: ['target', 'scenario', 'repetition', 'status', 'detail', 'measurement'],
  vectorReport: ['schemaVersion', 'runId', 'startedAt', 'options', 'datasetSha256', 'loadModel', 'hostOs', 'architecture',
    'logicalProcessors', 'runtime', 'storage', 'sourceRevision', 'targets', 'cases', 'provenance', 'loadGeneratorImage', 'vectorProfile'],
  vectorCase: ['target', 'scenario', 'repetition', 'status', 'detail', 'measurement', 'samples', 'vectorMetrics'],
  vectorProfile: ['id', 'recordCount', 'indexKind', 'queryMode', 'dimensions', 'metric', 'topK', 'seed', 'payloadBytes',
    'queryVectorCount', 'warmupQueries', 'measuredQueries', 'concurrency', 'timeoutSeconds', 'latencySampleCount',
    'repetitions', 'minimumRecall', 'updateCount'],
  vectorMetrics: ['recordCount', 'loadedRecordCount', 'queryAttempts', 'querySuccesses', 'updateAttempts', 'updateSuccesses',
    'exactRecall', 'minimumRecall', 'recallSamples', 'perQueryRecall', 'latencyP95Ms', 'latencyP99Ms', 'indexBuildMilliseconds', 'indexKind',
    'nativeIndexDefinition', 'nativeQueryPlan', 'indexParameters', 'serverMemoryBytes', 'serverMemorySamplingIntervalMs',
    'queryElapsedSeconds', 'queryUsefulOperationsPerSecond', 'updateElapsedSeconds', 'updateUsefulOperationsPerSecond'],
  measurement: ['attempts', 'successes', 'failures', 'elapsedSeconds', 'usefulOperationsPerSecond', 'latency',
    'uniqueCompletedMessages', 'enqueue', 'receive', 'ack', 'clientResources'],
  latency: ['p50Ms', 'p95Ms', 'p99Ms'],
  resources: ['cpuSeconds', 'allocatedBytes', 'peakObservedWorkingSetBytes', 'samplingIntervalMs'],
});

export const SUPPORT = Object.freeze({
  KeyLoad: [...ISOLATED.crud, ...ISOLATED.specialized],
  'PostgreSQL + pgvector': [...ISOLATED.crud, ...ISOLATED.specialized],
  Redis: ISOLATED.crud, Neo4j: [...ISOLATED.crud, 'GraphNeighbors', 'GraphTraverse'],
  MongoDB: [...ISOLATED.crud, 'GraphNeighbors', 'GraphTraverse', 'StreamAppend', 'StreamRead'],
  OpenSearch: [...ISOLATED.crud, 'VectorExact'], Qdrant: ['VectorExact'], RabbitMQ: ['QueueCycle'],
  KurrentDB: ['StreamAppend', 'StreamRead'],
  SurrealDB: [...ISOLATED.crud, 'GraphNeighbors', 'GraphTraverse', 'VectorExact'],
  HelixDB: [...ISOLATED.crud, 'GraphNeighbors', 'GraphTraverse'],
});

export const DOM = Object.freeze({
  scenario: 'isolated-scenario', nodes: 'isolated-node-count', target: 'isolated-target', metric: 'isolated-metric',
  repetition: 'isolated-repetition', results: 'isolated-results', error: 'isolated-error', retry: 'isolated-retry',
  announcement: 'isolated-announcement',
});

export function assertIsolated(condition) {
  if (!condition) {
    const error = new TypeError('Isolated comparison evidence is unavailable or invalid.');
    error.code = ISOLATED.error;
    throw error;
  }
}

export const exact = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value) &&
  Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
export const positive = value => Number.isSafeInteger(value) && value > 0;
export const text = value => typeof value === 'string' && value.trim().length > 0;
export const matches = (pattern, value) => typeof value === 'string' && pattern.test(value);
export const date = value => matches(ISOLATED.date, value) && Number.isFinite(Date.parse(value));
export const same = (left, right, keys) => keys.every(key => left[key] === right[key]);
export const runUrl = cohort => `https://github.com/${ISOLATED.repository}/actions/runs/${cohort.runId}`;
export const rawPath = id => `workers/${id}/worker.json`;
export const artifactUrl = (cohort, artifact) => `${runUrl(cohort)}/artifacts/${artifact.id}`;

const JOB_SCENARIOS = Object.freeze({
  PointRead: 'Point read', DocumentWrite: 'Document write', DocumentUpdate: 'Document update',
  DocumentDelete: 'Document delete', VectorExact: 'Exact vector search', QueueCycle: 'Queue cycle',
  GraphNeighbors: 'Graph neighbors', GraphTraverse: 'Graph traversal',
  StreamAppend: 'Stream append', StreamRead: 'Stream read',
});
const HISTORICAL_JOB_PREFIX = 'Benchmark / ';

export function isolatedJobLabel(cell, preflight = false) {
  const nodes = `${cell.nodeCount} node${cell.nodeCount === 1 ? '' : 's'}`;
  return preflight ? `Check / ${nodes}` : `${nodes} / ${JOB_SCENARIOS[cell.scenario]}`;
}

export const isolatedJobName = (cell, preflight = false) => `${cell.target} / ${isolatedJobLabel(cell, preflight)}`;
export const isolatedEvidenceJobName = cell => isolatedJobName(cell) +
  (cell.profile === ISOLATED.profile ? '' : ` / ${cell.profile}`);
// Original authenticated archives retain their exact historical display names.
export const matchesIsolatedJobName = (name, cell) =>
  name === isolatedJobName(cell) || name === HISTORICAL_JOB_PREFIX + cell.id;

export function isolatedCells() {
  return ISOLATED.targets.flatMap(target => ISOLATED.nodes.flatMap(nodeCount =>
    [...ISOLATED.crud, ...ISOLATED.specialized].map(scenario => ({
      id: `${target.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')}-n${nodeCount}-` +
        scenario.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase(),
      target, nodeCount, scenario, profile: ISOLATED.profile,
    }))));
}
