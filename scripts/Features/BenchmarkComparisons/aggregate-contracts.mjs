// Supplied proof is a contract input. Only the owning workflow authenticates GitHub transport.
export const AGGREGATE = Object.freeze({
  version: 4, proofVersion: 1, reportVersion: 3, repository: 'managedcode/KeyLoad', ref: 'refs/heads/main',
  workflow: 'Benchmarks', measured: 'measured', unsupported: 'unsupported', unsupportedTopology: 'unsupportedTopology',
  success: 'success', failed: 'failed', failure: 'failure',
  failureReason: 'Benchmark failed; no measurement data is available.', manifest: 'aggregate.json', workers: 'workers', raw: 'worker.json', encoding: 'utf8', hash: 'sha256',
  sha: /^[a-f0-9]{40}$/, digest: /^[a-f0-9]{64}$/, image: /@sha256:[a-f0-9]{64}$/,
  zipDigest: /^sha256:[a-f0-9]{64}$/, guid: /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i,
  safeId: /^[a-z0-9]+(?:-[a-z0-9]+)*$/, linux: /(?:^|\s|\/)(?:Linux|Ubuntu)(?:\s|$)/i,
  topology: Object.freeze({ 1: 'Single', 2: 'TwoNode', 3: 'Replicated' }),
  workerBytes: 67_108_864, metadataBytes: 4_194_304, totalBytes: 17_179_869_184,
  steps: Object.freeze(['Run database workload', 'Save benchmark results']),
  errors: Object.freeze({ input: 'E_AGGREGATE_INPUT', proof: 'E_AGGREGATE_PROOF', envelope: 'E_AGGREGATE_ENVELOPE',
    report: 'E_AGGREGATE_REPORT', output: 'E_AGGREGATE_OUTPUT', cohort: 'E_AGGREGATE_COHORT' }),
});

export const KEYS = Object.freeze({
  cell: ['id', 'target', 'nodeCount', 'scenario', 'profile', 'family'],
  envelope: ['schemaVersion', 'worker', 'disposition', 'reason', 'report'],
  worker: ['target', 'nodeCount', 'scenario', 'profile', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow', 'jobId'],
  cohort: ['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow', 'profile'],
  proof: ['schemaVersion', 'cohort', 'cells'], proofCell: ['id', 'job', 'artifact', 'workerSha256'],
  scaleProofCell: ['id', 'job', 'artifact', 'workerSha256', 'serverResource'],
  job: ['id', 'name', 'url', 'conclusion', 'steps'], step: ['name', 'conclusion'],
  artifact: ['id', 'name', 'sizeInBytes', 'digest', 'expired'],
  report: ['schemaVersion', 'runId', 'startedAt', 'options', 'datasetSha256', 'loadModel', 'hostOs', 'architecture',
    'logicalProcessors', 'runtime', 'storage', 'sourceRevision', 'targets', 'cases', 'provenance', 'loadGeneratorImage'],
  scaledReport: ['schemaVersion', 'runId', 'startedAt', 'options', 'datasetSha256', 'loadModel', 'hostOs', 'architecture',
    'logicalProcessors', 'runtime', 'storage', 'sourceRevision', 'targets', 'cases', 'provenance', 'loadGeneratorImage', 'scaledProfile'],
  vectorReport: ['schemaVersion', 'runId', 'startedAt', 'options', 'datasetSha256', 'loadModel', 'hostOs', 'architecture',
    'logicalProcessors', 'runtime', 'storage', 'sourceRevision', 'targets', 'cases', 'provenance', 'loadGeneratorImage', 'vectorProfile'],
  provenance: ['runId', 'attempt', 'repository', 'ref', 'workflow', 'profile'],
  target: ['name', 'version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization', 'image', 'cluster'],
  cluster: ['nodes', 'dataCopies', 'state', 'observations'],
  case: ['target', 'scenario', 'repetition', 'status', 'detail', 'measurement', 'samples'],
  scaledCase: ['target', 'scenario', 'repetition', 'status', 'detail', 'measurement', 'samples', 'scaled'],
  vectorCase: ['target', 'scenario', 'repetition', 'status', 'detail', 'measurement', 'samples', 'vectorMetrics'],
  sample: ['operation', 'worker', 'startedMs', 'completedMs', 'success', 'error', 'payloadBytes', 'completedMessageId', 'queue', 'latencyMs'],
  measurement: ['attempts', 'successes', 'failures', 'elapsedSeconds', 'usefulOperationsPerSecond', 'latency',
    'uniqueCompletedMessages', 'enqueue', 'receive', 'ack', 'clientResources'],
  latency: ['p50Ms', 'p95Ms', 'p99Ms'], queue: ['enqueueMs', 'receiveMs', 'ackMs'],
  scaledProfile: ['id', 'documents', 'operations', 'warmup', 'repetitions', 'concurrency', 'payloadBytes', 'seed',
    'dimensions', 'topK', 'timeoutSeconds', 'graphVertices', 'graphFanOut', 'graphDepth'],
  vectorProfile: ['id', 'recordCount', 'indexKind', 'queryMode', 'dimensions', 'metric', 'topK', 'seed', 'payloadBytes',
    'queryVectorCount', 'warmupQueries', 'measuredQueries', 'concurrency', 'timeoutSeconds', 'latencySampleCount',
    'repetitions', 'minimumRecall', 'updateCount'],
  vectorMetrics: ['recordCount', 'loadedRecordCount', 'queryAttempts', 'querySuccesses', 'updateAttempts', 'updateSuccesses',
    'exactRecall', 'minimumRecall', 'recallSamples', 'perQueryRecall', 'latencyP95Ms', 'latencyP99Ms', 'indexBuildMilliseconds',
    'indexKind', 'nativeIndexDefinition', 'nativeQueryPlan', 'indexParameters', 'serverMemoryBytes',
    'serverMemorySamplingIntervalMs', 'queryElapsedSeconds', 'queryUsefulOperationsPerSecond', 'updateElapsedSeconds',
    'updateUsefulOperationsPerSecond'],
  scaledCaseAccounting: ['requested', 'attempted', 'successes', 'failures', 'deadlineTimeouts', 'rejections', 'unfinished',
    'samplingAlgorithm', 'sampleCapacity', 'collectedSamples', 'missingSamples', 'latencyQuantileMethod'],
});

const CRUD = Object.freeze(['PointRead', 'DocumentWrite', 'DocumentUpdate', 'DocumentDelete']);
const GRAPH = Object.freeze(['GraphNeighbors', 'GraphTraverse']);
const EVENTS = Object.freeze(['StreamAppend', 'StreamRead']);
export const SUPPORT = Object.freeze({
  KeyLoad: [...CRUD, ...GRAPH, ...EVENTS, 'VectorExact', 'QueueCycle'],
  'PostgreSQL + pgvector': [...CRUD, ...GRAPH, ...EVENTS, 'VectorExact', 'QueueCycle'],
  Redis: CRUD, Neo4j: [...CRUD, ...GRAPH], MongoDB: [...CRUD, ...GRAPH, ...EVENTS],
  OpenSearch: [...CRUD, 'VectorExact'], Qdrant: ['VectorExact'], RabbitMQ: ['QueueCycle'], KurrentDB: EVENTS,
});

export const VECTOR_SUPPORT = Object.freeze({
  KeyLoad: [], 'PostgreSQL + pgvector': ['Exact', 'Hnsw', 'IvfFlat'], Qdrant: ['Exact', 'Hnsw'],
  RabbitMQ: [], Redis: [], Neo4j: [], MongoDB: [], OpenSearch: [], KurrentDB: [],
  SurrealDB: ['Exact', 'Hnsw'], HelixDB: ['NativeAnn'],
});

export function reject(code) {
  const error = new Error('Isolated comparison evidence rejected.');
  error.code = code;
  throw error;
}

export function requireValue(condition, code) {
  if (!condition) reject(code);
}

export function exactKeys(value, keys) {
  return value !== null && typeof value === 'object' && !Array.isArray(value) &&
    Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

export const text = value => typeof value === 'string' && value.trim().length > 0;
export const positive = value => Number.isSafeInteger(value) && value > 0;
export const matches = (pattern, value) => typeof value === 'string' && pattern.test(value);

export function validateCohort(value, profile) {
  requireValue(exactKeys(value, KEYS.cohort) && matches(AGGREGATE.sha, value.sourceRevision) && positive(value.runId) &&
    positive(value.attempt) && value.repository === AGGREGATE.repository && value.ref === AGGREGATE.ref &&
    value.workflow === AGGREGATE.workflow && value.profile === profile, AGGREGATE.errors.cohort);
  return value;
}
