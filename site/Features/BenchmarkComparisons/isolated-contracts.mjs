// UI identity and bounded browser errors; session owns the only measurement wire schema.
export const ISOLATED = Object.freeze({ repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks',
  error: 'E_ISOLATED_EVIDENCE', failed: 'failed', failure: 'failure', success: 'success' });
export const DOM = Object.freeze({
  summary: 'isolated-summary', scenario: 'isolated-scenario', nodes: 'isolated-node-count', metric: 'isolated-metric',
  type: 'isolated-type', results: 'isolated-results', error: 'isolated-error', retry: 'isolated-retry',
  announcement: 'isolated-announcement',
});
// Database families group engines for the type filter and the overview matrix; they are presentation labels only.
export const ENGINE_TYPES = Object.freeze({
  'KeyLoad': 'Multi-model', 'SurrealDB': 'Multi-model', 'PostgreSQL + pgvector': 'Relational', 'SQL Server': 'Relational',
  'MySQL': 'Relational', 'MongoDB': 'Document', 'Redis': 'Key-value', 'OpenSearch': 'Search', 'Neo4j': 'Graph',
  'HelixDB': 'Graph', 'TimescaleDB': 'Time series', 'InfluxDB': 'Time series', 'ClickHouse': 'Analytics',
  'Qdrant': 'Vector', 'RabbitMQ': 'Queue', 'KurrentDB': 'Event store',
});
export const TYPE_ORDER = Object.freeze(['Multi-model', 'Relational', 'Document', 'Key-value', 'Search', 'Graph',
  'Time series', 'Analytics', 'Vector', 'Queue', 'Event store']);
export const SCENARIOS = Object.freeze([
  ['load-million', 'Load 1M'], ['index', 'Index build'], ['sequential-read', 'Sequential read'],
  ['random-read', 'Random read'], ['search', 'Search'], ['verify-final', 'Full readback'], ['mixed/create', 'Create'],
  ['mixed/delete', 'Delete'], ['mixed/update', 'Update'], ['mixed/read', 'Read'], ['mixed/search', 'Search'],
].map(([value, label]) => Object.freeze({ value, label, mixed: value.startsWith('mixed/') })));
// Index build and final readback record elapsed time only; every other scenario has per-request measurements.
export const durationOnly = scenario => scenario === 'index' || scenario === 'verify-final';
export const scenarioTitle = value => {
  const scenario = SCENARIOS.find(item => item.value === value);
  return scenario.mixed ? 'Mixed workload · ' + scenario.label.toLowerCase() : scenario.label;
};
export function assertIsolated(condition) {
  if (!condition) {
    const error = new TypeError('Benchmark session evidence is unavailable or invalid.');
    error.code = ISOLATED.error;
    throw error;
  }
}
export const runUrl = cohort => `https://github.com/${ISOLATED.repository}/actions/runs/${cohort.runId}`;
export const artifactUrl = (cohort, artifact) => `${runUrl(cohort)}/artifacts/${artifact.id}`;
