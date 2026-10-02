export const schemaVersion = 3;
export const repositoryName = 'managedcode/KeyLoad';
export const mainRef = 'refs/heads/main';
export const workflowName = 'KeyLoad CI';
export const legacySourceSha = '9c570f8c33a7a9667507a8e1c0ca68860de3be45';
export const legacyRunId = 36926803549;
export const legacyAttempt = 1;
export const legacyTopology = 'KeyLoad is RF3; external engines are single-node historical baselines.';
export const sourceShaPattern = /^[a-f0-9]{40}$/i;
export const digestPattern = /(?:^|@)sha256:[a-f0-9]{64}$/i;
export const scenarioName = Object.freeze({
  pointRead: 'PointRead',
  documentWrite: 'DocumentWrite',
  vectorExact: 'VectorExact',
  queueCycle: 'QueueCycle',
  graphNeighbors: 'GraphNeighbors',
  graphTraverse: 'GraphTraverse',
  streamAppend: 'StreamAppend',
  streamRead: 'StreamRead',
});
export const targetName = Object.freeze({
  keyLoad: 'KeyLoad',
  postgres: 'PostgreSQL + pgvector',
  qdrant: 'Qdrant',
  rabbitMq: 'RabbitMQ',
  redis: 'Redis',
  neo4j: 'Neo4j',
  mongoDb: 'MongoDB',
  openSearch: 'OpenSearch',
  kurrentDb: 'KurrentDB',
});
export const topologyName = Object.freeze({ single: 'Single', replicated: 'Replicated' });
export const caseStatus = Object.freeze({ measured: 'measured', unsupported: 'unsupported', unavailable: 'unavailable' });
export const runConclusion = Object.freeze({ success: 'success' });
export const triggerEvent = Object.freeze({ push: 'push', workflowDispatch: 'workflow_dispatch' });
export const queueStage = Object.freeze({ enqueue: 'enqueue', receive: 'receive', ack: 'ack' });
export const cliOption = Object.freeze({
  sourceSha: '--source-sha', runId: '--run-id', attempt: '--attempt', repository: '--repository', conclusion: '--conclusion',
  event: '--event', ref: '--ref', workflow: '--workflow', input: '--input', output: '--output', legacyBaseline: '--legacy-baseline',
});
export const cliSyntax = Object.freeze({ optionPrefix: cliOption.sourceSha.slice(0, 2) });
export const artifactLayout = Object.freeze({
  branch: 'benchmark-results', artifactsDirectory: 'artifacts', historyDirectory: 'history', latestDirectory: 'latest',
  chartsDirectory: 'charts', resultsDirectory: 'results', latestStagingPrefix: '.latest-staging-', latestBackupPrefix: '.latest-backup-',
  temporaryRoot: '/private/tmp', parentPathSegment: '..', currentPathSegment: '.', pathSeparator: '/',
  scriptToRepositoryRoot: '../../..',
});
export const outputFile = Object.freeze({
  results: 'results.json', summary: 'summary.json', provenance: 'provenance.json', index: 'index.json', checksum: 'sha256.json',
  throughputChart: 'throughput.svg', p99Chart: 'p99.svg', queueChart: 'queue-stage-p99.svg',
});
export const outputFormat = Object.freeze({ jsonIndent: 2, jsonSuffix: '.json', newline: '\n' });
export const publisherField = Object.freeze({
  trustedWorkflowGateRequired: 'trustedWorkflowGateRequired', publisherNote: 'publisherNote', qualificationNote: 'qualificationNote',
  chartPaths: 'chartPaths',
  results: artifactLayout.resultsDirectory, charts: artifactLayout.chartsDirectory, url: 'url', path: 'path', file: 'file',
  profileCount: 'profileCount', caseCount: 'caseCount', rawSha256: 'rawSha256',
});
export const summaryField = Object.freeze({
  throughput: 'throughput', p99Ms: 'p99Ms', queueP99Ms: 'queueP99Ms', median: 'median', minimum: 'minimum', maximum: 'maximum',
  supportedCaseCount: 'supportedCaseCount', unavailableCaseCount: 'unavailableCaseCount',
  targetCount: 'targetCount', scenarioCount: 'scenarioCount',
});
export const chartMetric = Object.freeze({ throughput: summaryField.throughput, p99: 'p99' });
export const chartColor = Object.freeze({ teal: '#187f72', blue: '#3f69bd', amber: '#aa5b20' });
export const dataEncoding = Object.freeze({ utf8: 'utf8', hex: 'hex' });
export const fileSystemSignal = Object.freeze({ notFound: 'ENOENT', createNew: 'wx' });
export const javascriptType = Object.freeze({ string: 'string', number: 'number', boolean: 'boolean', object: 'object' });
export const messageLabel = Object.freeze({ loadGenerator: 'load generator' });
export const profileName = Object.freeze({
  smoke: 'smoke', json1kC8: 'json-1k-c8', json16kC4: 'json-16k-c4',
  smokeSingle: 'smoke-single', json1kC8Single: 'json-1k-c8-single', json16kC4Single: 'json-16k-c4-single',
  smokeReplicated: 'smoke-replicated', json1kC8Replicated: 'json-1k-c8-replicated', json16kC4Replicated: 'json-16k-c4-replicated',
});
export const schemaField = Object.freeze({
  schemaVersion: 'schemaVersion', sourceRevision: 'sourceRevision', datasetSha256: 'datasetSha256', corpusSha256: 'corpusSha256', loadGeneratorImage: 'loadGeneratorImage',
  options: 'options', topology: 'topology', targets: 'targets', cases: 'cases', target: 'target', scenario: 'scenario',
  repetition: 'repetition', status: 'status', measurement: 'measurement', samples: 'samples', detail: 'detail', payloadBytes: 'payloadBytes',
  operations: 'operations', concurrency: 'concurrency', repetitions: 'repetitions', scenarios: 'scenarios', seed: 'seed', timeoutSeconds: 'timeoutSeconds',
  nodes: 'nodes', dataCopies: 'dataCopies', state: 'state', observations: 'observations', cluster: 'cluster',
  operation: 'operation', worker: 'worker', startedMs: 'startedMs', completedMs: 'completedMs', success: 'success', error: 'error',
  completedMessageId: 'completedMessageId', queue: 'queue', enqueueMs: 'enqueueMs', receiveMs: 'receiveMs', ackMs: 'ackMs', latencyMs: 'latencyMs',
  attempts: 'attempts', successes: 'successes', failures: 'failures', elapsedSeconds: 'elapsedSeconds', usefulOperationsPerSecond: 'usefulOperationsPerSecond',
  uniqueCompletedMessages: 'uniqueCompletedMessages', latency: 'latency', clientResources: 'clientResources', p50Ms: 'p50Ms', p95Ms: 'p95Ms', p99Ms: 'p99Ms',
  cpuSeconds: 'cpuSeconds', allocatedBytes: 'allocatedBytes', peakObservedWorkingSetBytes: 'peakObservedWorkingSetBytes', samplingIntervalMs: 'samplingIntervalMs',
  runId: 'runId', attempt: 'attempt', repository: 'repository', ref: 'ref', workflow: 'workflow', profile: 'profile', event: 'event', conclusion: 'conclusion', provenance: 'provenance',
  sourceSha: 'sourceSha', legacyBaseline: 'legacyBaseline', qualifiesSchema3: 'qualifiesSchema3', profiles: 'profiles', rawUrl: 'rawUrl', sha256: 'sha256',
  name: 'name', image: 'image', version: 'version', writeAcknowledgement: 'writeAcknowledgement', readContract: 'readContract', transport: 'transport', authorization: 'authorization',
  startedAt: 'startedAt', loadModel: 'loadModel', hostOs: 'hostOs', architecture: 'architecture', runtime: 'runtime', storage: 'storage', logicalProcessors: 'logicalProcessors',
});
export const hashAlgorithm = Object.freeze({ sha256: schemaField.sha256 });
export const scenarioNames = Object.freeze(Object.values(scenarioName));
export const targetNames = Object.freeze(Object.values(targetName));
export const singleSupport = Object.freeze({
  [targetName.keyLoad]: scenarioNames,
  [targetName.postgres]: scenarioNames,
  [targetName.qdrant]: [scenarioName.vectorExact],
  [targetName.rabbitMq]: [scenarioName.queueCycle],
  [targetName.redis]: [scenarioName.pointRead, scenarioName.documentWrite],
  [targetName.neo4j]: [scenarioName.pointRead, scenarioName.documentWrite, scenarioName.graphNeighbors, scenarioName.graphTraverse],
  [targetName.mongoDb]: [scenarioName.pointRead, scenarioName.documentWrite, scenarioName.graphNeighbors, scenarioName.graphTraverse, scenarioName.streamAppend, scenarioName.streamRead],
  [targetName.openSearch]: [scenarioName.pointRead, scenarioName.documentWrite, scenarioName.vectorExact],
  [targetName.kurrentDb]: [scenarioName.streamAppend, scenarioName.streamRead],
});
export const replicatedSupport = Object.freeze({
  ...singleSupport,
  [targetName.neo4j]: [],
});
const smokeOptions = Object.freeze({ documents: 32, operations: 12, warmup: 2, repetitions: 2, concurrency: 2, payloadBytes: 1024, dimensions: 8, topK: 3, graphVertices: 256, graphFanOut: 3, graphDepth: 3 });
const smallOptions = Object.freeze({ documents: 128, operations: 60, warmup: 5, repetitions: 3, concurrency: 8, payloadBytes: 1024, dimensions: 32, topK: 10, graphVertices: 128, graphFanOut: 3, graphDepth: 3 });
const largeOptions = Object.freeze({ ...smallOptions, concurrency: 4, payloadBytes: 16384, graphDepth: 5 });
export const legacyProfiles = Object.freeze({
  [profileName.smoke]: Object.freeze({ options: smokeOptions, topology: topologyName.single, displayTopology: legacyTopology, charts: false }),
  [profileName.json1kC8]: Object.freeze({ options: smallOptions, topology: topologyName.single, displayTopology: legacyTopology, charts: true }),
  [profileName.json16kC4]: Object.freeze({ options: largeOptions, topology: topologyName.single, displayTopology: legacyTopology, charts: true }),
});
export const schema3Profiles = Object.freeze({
  [profileName.smokeSingle]: Object.freeze({ options: smokeOptions, topology: topologyName.single, charts: false }),
  [profileName.json1kC8Single]: Object.freeze({ options: smallOptions, topology: topologyName.single, charts: true }),
  [profileName.json16kC4Single]: Object.freeze({ options: largeOptions, topology: topologyName.single, charts: true }),
  [profileName.smokeReplicated]: Object.freeze({ options: smokeOptions, topology: topologyName.replicated, charts: false }),
  [profileName.json1kC8Replicated]: Object.freeze({ options: smallOptions, topology: topologyName.replicated, charts: true }),
  [profileName.json16kC4Replicated]: Object.freeze({ options: largeOptions, topology: topologyName.replicated, charts: true }),
});
export const legacyTargetNames = Object.freeze(targetNames.slice(0, 6));
export const legacyScenarioNames = Object.freeze(scenarioNames.slice(0, 6));
export const legacySupportedScenarios = Object.freeze({
  [targetName.keyLoad]: legacyScenarioNames,
  [targetName.postgres]: legacyScenarioNames,
  [targetName.qdrant]: [scenarioName.vectorExact],
  [targetName.rabbitMq]: [scenarioName.queueCycle],
  [targetName.redis]: [scenarioName.pointRead, scenarioName.documentWrite],
  [targetName.neo4j]: [scenarioName.pointRead, scenarioName.documentWrite, scenarioName.graphNeighbors, scenarioName.graphTraverse],
});
export const emptyString = '';
export const caseKeyDelimiter = '\u0000';
export const chartLayout = Object.freeze({
  minimumAxisMaximum: 1,
  width: 1440, height: 980, panelWidth: 345, panelHeight: 430, panelGap: 12, chartLeft: 42, chartTop: 88, plotHeight: 250,
  labelFont: 11, titleFont: 19, smallFont: 10, queueWidth: 1280, queueHeight: 620,
  panelColumns: 4, panelOriginX: 12, panelOriginY: 76, panelTitleOffsetX: 14, panelTitleOffsetY: 34, panelTitleFont: 13, barCenterFraction: 0.5,
  scenarioTickCount: 4, scenarioTickLabelOffsetX: 5, scenarioTickLabelOffsetY: 3, barMaximumWidth: 17, barSlotFraction: 0.55,
  unavailableScenarioOffsetY: 7, rangeCapHalfWidth: 4, scenarioLabelOffsetX: 4, scenarioLabelOffsetY: 14, scenarioLabelRotation: -55,
  chartTitleX: 22, chartTitleY: 34, chartSubtitleY: 58,
  queueLeft: 84, queueTop: 118, queuePlotHeight: 370, queueRight: 42, queueTickCount: 5, queueTickLabelOffsetX: 7,
  unavailableQueueOffsetY: 6, queueBarWidth: 16, queueStageCenterOffset: 1, queueBarGap: 5,
  queueLabelOffsetX: 18, queueLabelOffsetY: 18, queueLabelRotation: -42, queueLegendSpacing: 150,
  queueLegendY: 48, queueLegendSwatchSize: 14, queueLegendTextOffsetX: 20, queueLegendTextOffsetY: 36,
  queueTitleX: 24, queueTitleY: 36, queueSubtitleY: 62,
});
