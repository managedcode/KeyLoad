// Current session cohort only. Physical isolation is database × execution topology.
export const SESSION = Object.freeze({
  schema: 'session', profile: 'session', repository: 'managedcode/KeyLoad',
  ref: 'refs/heads/main', workflow: 'Benchmarks', workers: 32,
  records: 1_000_000, operations: 100_000, concurrency: 16, warmup: 0,
  caseIds: Object.freeze(['load-million', 'index', 'sequential-read', 'random-read', 'search', 'mixed', 'verify-final']),
  nodes: Object.freeze([1, 3]), artifactPrefix: 'comparison-session-', aggregateArtifact: 'comparison-session-suite',
  aggregateJob: 'Combine benchmark results', prepareJob: 'Prepare comparison runtime',
  prepareSteps: Object.freeze(['Build the complete solution once', 'Check code formatting', 'Check repository rules',
    'Test benchmark contracts', 'Test canonical benchmark worker selection',
    'Test prompt termination on Aspire resource failures', 'Check execution benchmark preparation contracts',
    'Verify GitHub job identity', 'Save compiled runtime identities', 'Save the compiled benchmark runtime']),
  workerSteps: Object.freeze(['Run database workload', 'Save benchmark results']),
  reportBytes: 268_435_456, runtimeBytesCore: 16_777_216, sessionBytes: 4_194_304,
  aggregateBytes: 33_554_432, archiveBytes: 536_870_912, allArchiveBytes: 17_179_869_184,
  capabilitiesUnavailable: 'SessionCapabilitiesUnavailable', topologyUnavailable: 'RuntimeTopologyUnavailable',
  unavailableTopologyTargets: Object.freeze(['Neo4j','SurrealDB','HelixDB','TimescaleDB','SQL Server','MySQL','ClickHouse','InfluxDB']),
  failure: 'Benchmark session evidence rejected.',
});
export const SESSION_TARGETS = Object.freeze([
  ['keyload', 'KeyLoad'], ['postgresql', 'PostgreSQL + pgvector'], ['qdrant', 'Qdrant'],
  ['rabbitmq', 'RabbitMQ'], ['redis', 'Redis'], ['neo4j', 'Neo4j'], ['mongodb', 'MongoDB'],
  ['opensearch', 'OpenSearch'], ['kurrentdb', 'KurrentDB'], ['surrealdb', 'SurrealDB'], ['helixdb', 'HelixDB'],
  ['timescaledb', 'TimescaleDB'], ['sqlserver', 'SQL Server'], ['mysql', 'MySQL'],
  ['clickhouse', 'ClickHouse'], ['influxdb', 'InfluxDB'],
].map(([key, name]) => Object.freeze({ key, name })));
export const requireSession = condition => { if (!condition) throw new Error(SESSION.failure); };
export const exactSession = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value)
  && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
export const sessionHash = value => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);
export const sessionPositive = value => Number.isSafeInteger(value) && value > 0;
export const sessionCount = value => Number.isSafeInteger(value) && value >= 0;
export const sessionPath = value => typeof value === 'string' && value.length <= 512
  && /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/.test(value)
  && !value.split('/').some(part => part === '.' || part === '..');
export const sessionText = value => typeof value === 'string' && value.length > 0 && value.length <= 1024
  && !/[\u0000-\u001f\u007f]/.test(value);
export const sessionUnavailableTopology = row => row.nodeCount === 3 && SESSION.unavailableTopologyTargets.includes(row.target);
export const sessionJobName = row => `${row.target} / Session / ${row.nodeCount} node${row.nodeCount === 1 ? '' : 's'}`;
export function sessionRows() {
  return SESSION_TARGETS.flatMap(target => SESSION.nodes.map(nodeCount => {
    const id = `${target.key}-n${nodeCount}`;
    const row = { id, target: target.name, nodeCount, artifactName: SESSION.artifactPrefix + id };
    return { ...row, jobName: sessionJobName(row) };
  }));
}
export function validateSessionCohort(cohort) {
  requireSession(exactSession(cohort, ['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow', 'profile'])
    && /^[a-f0-9]{40}$/.test(cohort.sourceRevision ?? '') && sessionPositive(cohort.runId)
    && sessionPositive(cohort.attempt) && cohort.repository === SESSION.repository && cohort.ref === SESSION.ref
    && cohort.workflow === SESSION.workflow && cohort.profile === SESSION.profile);
  return cohort;
}
