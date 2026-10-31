// One result file per Benchmarks job: one database, data type and node count. KeyLoad runs every type's scenario.
export const NODES = Object.freeze([1, 3]);
export const KEYLOAD = 'KeyLoad';

// Case kinds pick the column colour only. A case measured once, such as an index build, compares its seconds.
const type = (id, label, cases, databases) => Object.freeze({ id, label, databases: Object.freeze([KEYLOAD, ...databases]),
  cases: Object.freeze(cases.map(([caseId, caseLabel, kind]) => Object.freeze({ id: caseId, label: caseLabel, kind,
    seconds: kind === 'build' }))) });
export const TYPES = Object.freeze([
  type('documents', 'Documents', [['load', 'Load', 'write'], ['point-read', 'Point read', 'read'],
    ['range-query', 'Range query', 'query'], ['update', 'Update', 'write'], ['delete', 'Delete', 'write'],
    ['mixed', 'Mixed', 'mixed']], ['MongoDB', 'SurrealDB']),
  type('key-value', 'Key-value', [['load', 'Load', 'write'], ['get', 'Get', 'read'], ['set', 'Set', 'write'],
    ['delete', 'Delete', 'write'], ['mixed', 'Mixed', 'mixed']], ['Redis']),
  type('relational', 'Relational', [['load', 'Load', 'write'], ['pk-select', 'Primary-key select', 'read'],
    ['range-select', 'Range select', 'query'], ['update', 'Update', 'write'], ['delete', 'Delete', 'write'],
    ['mixed', 'Mixed', 'mixed']], ['PostgreSQL + pgvector', 'SQL Server', 'MySQL']),
  type('graph', 'Graph', [['load', 'Load', 'write'], ['neighbors', 'Neighbors', 'read'], ['traverse', 'Traverse', 'query']],
    ['Neo4j', 'HelixDB', 'SurrealDB']),
  type('vector', 'Vector', [['load', 'Load', 'write'], ['exact-top10', 'Exact top 10', 'vector']],
    ['Qdrant', 'PostgreSQL + pgvector']),
  type('search', 'Search', [['load', 'Load', 'write'], ['index', 'Index build', 'build'], ['term-query', 'Term query', 'query'],
    ['and-query', 'AND query', 'query']], ['OpenSearch']),
  type('time-series', 'Time series', [['load', 'Load', 'write'], ['range-read', 'Range read', 'read'],
    ['aggregate', 'Aggregate', 'query'], ['windows', 'Windows', 'query']], ['TimescaleDB', 'InfluxDB']),
  type('analytics', 'Analytics', [['load', 'Load', 'write'], ['group-by', 'Group by', 'query'],
    ['filtered-aggregate', 'Filtered aggregate', 'query']], ['ClickHouse']),
  type('queue', 'Queue', [['cycle', 'Publish, receive, ACK', 'queue']], ['RabbitMQ']),
  type('events', 'Events', [['load', 'Load', 'write'], ['append', 'Append', 'write'], ['read', 'Read', 'read'],
    ['mixed', 'Mixed', 'mixed']], ['KurrentDB']),
]);

const RESULT_KEYS = Object.freeze(['database', 'type', 'nodes', 'image', 'commit', 'runId', 'startedAt', 'finishedAt',
  'records', 'operations', 'clients', 'error', 'cases']);
const CASE_KEYS = Object.freeze(['id', 'status', 'operations', 'errors', 'seconds', 'opsPerSecond', 'p50Ms', 'p95Ms',
  'p99Ms', 'batchSize', 'error']);
const COUNTS = Object.freeze(['operations', 'errors']);
const MEASURES = Object.freeze(['seconds', 'opsPerSecond', 'p50Ms', 'p95Ms', 'p99Ms']);

export const findType = id => TYPES.find(item => item.id === id);
export const resultKey = result => `${result.type}|${result.database}|${result.nodes}`;

// The harness names each file after its own content, for example postgresql-pgvector-vector-n3.json.
export const RESULT_NAME = new RegExp(`-(?:${TYPES.map(item => item.id).join('|')})-n\\d+\\.json$`);
export const resultFileName = result => result.database.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') +
  `-${result.type}-n${result.nodes}.json`;

function check(condition, label, field) {
  if (!condition) throw new TypeError(`${label}: invalid ${field}.`);
}

const isObject = value => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value, keys) => isObject(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const text = value => typeof value === 'string' && value.trim().length > 0;
const count = value => Number.isSafeInteger(value) && value >= 0;
const measure = value => Number.isFinite(value) && value >= 0;
const instant = value => text(value) && /^\d{4}-\d\d-\d\dT/.test(value) && Number.isFinite(Date.parse(value));

// Every case records its values; a failed case also carries the first error it met.
function validateCase(value, label, kind) {
  check(exact(value, CASE_KEYS), label, 'case');
  const field = `case ${typeof value.id === 'string' ? value.id : '?'}`;
  check(kind.cases.some(item => item.id === value.id), label, field + ' id');
  check(value.status === 'passed' || value.status === 'failed', label, field + ' status');
  for (const key of COUNTS) check(count(value[key]), label, `${field} ${key}`);
  for (const key of MEASURES) check(measure(value[key]), label, `${field} ${key}`);
  check(count(value.batchSize) && value.batchSize > 0, label, field + ' batchSize');
  check(value.status === 'passed' ? value.error === null : text(value.error), label, field + ' error');
}

export function validateResult(value, label) {
  check(exact(value, RESULT_KEYS), label, 'result fields');
  const kind = findType(value.type);
  check(kind !== undefined, label, 'type');
  check(kind.databases.includes(value.database), label, 'database');
  check(NODES.includes(value.nodes), label, 'nodes');
  // Image, commit, run and finish time are null or empty when the job could not observe them, such as on a local run.
  for (const key of ['image', 'commit']) check(value[key] === null || typeof value[key] === 'string', label, key);
  check(value.error === null || text(value.error), label, 'error');
  check(value.runId === null || count(value.runId), label, 'runId');
  check(instant(value.startedAt), label, 'startedAt');
  check(value.finishedAt === null || instant(value.finishedAt), label, 'finishedAt');
  for (const key of ['records', 'operations', 'clients']) check(count(value[key]) && value[key] > 0, label, key);
  check(Array.isArray(value.cases), label, 'cases');
  for (const item of value.cases) validateCase(item, label, kind);
  check(new Set(value.cases.map(item => item.id)).size === value.cases.length, label, 'case ids');
  return value;
}

// Merged files keep one result per database, type and node count, in type, database and node order.
export function compareResults(left, right) {
  const typeOrder = TYPES.findIndex(item => item.id === left.type) - TYPES.findIndex(item => item.id === right.type);
  if (typeOrder !== 0) return typeOrder;
  const databases = findType(left.type).databases;
  return databases.indexOf(left.database) - databases.indexOf(right.database) || left.nodes - right.nodes;
}

export function validateMerged(value, label) {
  check(exact(value, ['results']) && Array.isArray(value.results) && value.results.length > 0, label, 'results');
  const keys = new Set();
  value.results.forEach((result, index) => {
    validateResult(result, `${label} results[${index}]`);
    check(!keys.has(resultKey(result)), `${label} results[${index}]`, 'database, type and nodes');
    keys.add(resultKey(result));
  });
  return value;
}
