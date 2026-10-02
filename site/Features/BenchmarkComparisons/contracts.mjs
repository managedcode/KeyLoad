export const IDS = Object.freeze({
  lab: 'benchmarks', scene: 'cluster-scene', motion: 'scene-motion',
  profile: 'profile', metric: 'metric', repetition: 'repetition', logScale: 'log-scale',
  panel: 'results-panel', chart: 'chart', chartTitle: 'chart-title', description: 'scenario-description',
  direction: 'direction', metricNote: 'metric-note', conditions: 'conditions', table: 'result-table',
  profiles: 'engine-profiles', error: 'load-error', publishedDate: 'published-date', host: 'host-summary',
  revision: 'revision-summary', siteRevision: 'site-revision-summary', workflow: 'workflow-link',
  json: 'json-download', csv: 'csv-download', report: 'report-download',
  engineCount: 'recorded-engine-count', announcement: 'measurement-announcement', retry: 'retry-measurements',
});

export const SELECTORS = Object.freeze({
  scenario: '[data-scenario]', command: '.command-box code', copy: '#copy-command',
  poster: '.cluster-poster', sceneStatus: '[data-scene-status]',
});

export const CONFIG = Object.freeze({
  catalogUrl: './data/catalog.json', dataDirectory: './data/', repository: 'managedcode/KeyLoad',
  repositoryUrl: 'https://github.com/managedcode/KeyLoad', actionsUrl: 'https://github.com/managedcode/KeyLoad/actions',
  schemaVersion: 2, catalogSchemaVersion: 1, targetCount: 6, scenarioCount: 6, supportedCases: 20,
  maximumReportBytes: 67108864, maximumCatalogBytes: 262144, median: 'median',
  defaultScenario: 'PointRead', defaultMetric: 'throughput', queueScenario: 'QueueCycle',
  measured: 'measured', unsupported: 'unsupported', failed: 'failed',
  units: Object.freeze({ millisecondsPerSecond: 1000, bytesPerKiB: 1024, bytesPerMiB: 1048576, percent: 100 }),
  numberLocale: 'en', dateTimeZone: 'UTC', axisTicks: 4, copyResetMs: 1800,
  sha256: 'SHA-256', utf8: 'utf-8', cache: 'no-cache',
  files: Object.freeze({ json: 'results.json', csv: 'samples.csv', report: 'results.md' }),
  engines: Object.freeze(['KeyLoad', 'PostgreSQL + pgvector', 'Qdrant', 'RabbitMQ', 'Redis', 'Neo4j']),
  reportPattern: /^runs\/[a-z0-9-]+\/results\.json$/,
  idPattern: /^[a-z0-9-]+$/, revisionPattern: /^[a-f0-9]{40}$/, hashPattern: /^[a-f0-9]{64}$/,
  evidencePattern: /^https:\/\/github\.com\/managedcode\/KeyLoad\/actions\/runs\/[1-9]\d*$/,
});

export const TEXT = Object.freeze({
  loading: 'Loading verified measurements…', loadingProfile: 'Loading this workload profile…',
  unavailable: 'Measurements unavailable',
  loadFailure: 'Published measurements could not be loaded. Open the GitHub evidence or retry.',
  missingCatalog: 'No published measurement catalog is available.', invalidCatalog: 'The measurement catalog is invalid.',
  invalidReport: 'The historical report is invalid or incomplete.', invalidSchema: 'This report schema is not supported.',
  invalidPath: 'The report or evidence path is outside the accepted source boundary.',
  invalidHash: 'The raw report hash does not match its catalog entry.', invalidRevision: 'The measured source revision does not match.',
  fetchFailure: 'The selected measurement file is unavailable.', tooLarge: 'The measurement file exceeds its size bound.',
  unsupported: 'Unsupported', notMeasured: 'Not measured', failed: 'Failed', verified: 'Verified',
  median: 'Median of repetitions', repetition: index => `Repetition ${index + 1}`,
  source: revision => `Measured source ${revision.slice(0, 12)}`,
  corpus: hash => `Corpus ${hash.slice(0, 12)}`,
  localPreview: 'Website preview · uncommitted working tree',
  siteSource: revision => `Website source ${revision.slice(0, 12)}`,
  generatorNote: 'These measurements cover the load generator, drivers and sampler. RSS includes process history and is sampled every 50 ms; it is not database RAM.',
  failureNote: 'Failure rate retains every selected attempt. Failed and timed-out requests stay in the denominator.',
  medianNote: 'Bars show median per-repetition values; whiskers show their full range. Latency values are per-run percentiles, not pooled percentiles.',
  repetitionNote: 'One complete repetition is selected. Setup, warmup and correctness checks are excluded from the timer.',
  unrecorded: '—', sourceCheckout: 'Source checkout', copied: 'Copied', selected: 'Selected', copy: 'Copy',
});

export const DOM = Object.freeze({
  tags: Object.freeze({ span: 'span', div: 'div', p: 'p', small: 'small', h3: 'h3', article: 'article', details: 'details',
    summary: 'summary', dl: 'dl', dt: 'dt', dd: 'dd', tr: 'tr', td: 'td' }),
  attributes: Object.freeze({ hidden: 'aria-hidden', disabled: 'aria-disabled', labelledBy: 'aria-labelledby',
    selected: 'aria-selected', busy: 'aria-busy', href: 'href' }),
  events: Object.freeze({ change: 'change', click: 'click', keydown: 'keydown' }),
  keys: Object.freeze({ right: 'ArrowRight', left: 'ArrowLeft', home: 'Home', end: 'End' }),
  classes: Object.freeze({ dot: 'engine-dot', empty: 'empty-state', row: 'bar-row', keyload: 'keyload',
    unsupported: 'unsupported-row', label: 'bar-label', track: 'bar-track', value: 'bar-value', fill: 'bar-fill',
    range: 'bar-range', axis: 'bar-axis', axisLabels: 'axis-labels', card: 'engine-card', version: 'version',
    keyloadTable: 'keyload-table-row', cellEngine: 'cell-engine', status: 'result-status' }),
  properties: Object.freeze({ engine: '--engine', width: '--width', minimum: '--min', range: '--range' }),
});

export const CONDITIONS = Object.freeze({ documents: 'Documents', payload: 'Payload', concurrency: 'Concurrency',
  attempts: 'Attempts/case', warmup: 'Warmup', repetitions: 'Repetitions', graph: 'Graph', vectors: 'Vectors' });

export const PROFILE_LABELS = Object.freeze({
  summary: 'Guarantees & configuration', write: 'Write acknowledgement', reads: 'Reads', transport: 'Transport',
  authorization: 'Authorization', image: 'Pinned image',
});
