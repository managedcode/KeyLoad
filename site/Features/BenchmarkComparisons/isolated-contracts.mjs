// UI identity and bounded browser errors; session owns the only measurement wire schema.
export const ISOLATED = Object.freeze({ repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks',
  error: 'E_ISOLATED_EVIDENCE', failed: 'failed', failure: 'failure', success: 'success' });
export const DOM = Object.freeze({
  summary: 'isolated-summary', type: 'isolated-type', results: 'isolated-results', error: 'isolated-error',
  retry: 'isolated-retry', announcement: 'isolated-announcement',
});
// Database families group engines for the type filter; they are presentation labels only.
export const ENGINE_TYPES = Object.freeze({
  'KeyLoad': 'Multi-model', 'SurrealDB': 'Multi-model', 'PostgreSQL + pgvector': 'Relational', 'SQL Server': 'Relational',
  'MySQL': 'Relational', 'MongoDB': 'Document', 'Redis': 'Key-value', 'OpenSearch': 'Search', 'Neo4j': 'Graph',
  'HelixDB': 'Graph', 'TimescaleDB': 'Time series', 'InfluxDB': 'Time series', 'ClickHouse': 'Analytics',
  'Qdrant': 'Vector', 'RabbitMQ': 'Queue', 'KurrentDB': 'Event store',
});
export const TYPE_ORDER = Object.freeze(['Multi-model', 'Relational', 'Document', 'Key-value', 'Search', 'Graph',
  'Time series', 'Analytics', 'Vector', 'Queue', 'Event store']);
// Index build and full readback record elapsed time only; every other test has per-request measurements.
export const TESTS = Object.freeze([
  ['load-million', 'Load 1M'], ['index', 'Index build'], ['sequential-read', 'Sequential read'], ['random-read', 'Random read'],
  ['search', 'Search'], ['mixed/create', 'Mixed create'], ['mixed/delete', 'Mixed delete'], ['mixed/update', 'Mixed update'],
  ['mixed/read', 'Mixed read'], ['mixed/search', 'Mixed search'], ['verify-final', 'Full readback'],
].map(([id, label]) => Object.freeze({ id, label, seconds: id === 'index' || id === 'verify-final' })));
// The overview reads one test per operation family; mixed spans all five concurrent lanes of the mixed test.
export const GENERAL = Object.freeze([
  ['write', 'Write', 'load-million', 'Bulk insert, 1M documents'], ['read', 'Read', 'random-read', 'Random key lookups'],
  ['search', 'Search', 'search', 'Indexed search queries'], ['mixed', 'Mixed', 'mixed', '500k mixed operations, 16 clients'],
].map(([key, label, id, hint]) => Object.freeze({ key, label, id, hint })));
export function assertIsolated(condition) {
  if (!condition) {
    const error = new TypeError('Benchmark session evidence is unavailable or invalid.');
    error.code = ISOLATED.error;
    throw error;
  }
}
export const runUrl = cohort => `https://github.com/${ISOLATED.repository}/actions/runs/${cohort.runId}`;
export const artifactUrl = (cohort, artifact) => `${runUrl(cohort)}/artifacts/${artifact.id}`;
