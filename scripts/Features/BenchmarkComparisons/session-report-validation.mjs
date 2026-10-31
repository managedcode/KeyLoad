import { SESSION, exactSession as exact, requireSession as require, sessionHash, sessionPath,
  sessionPositive, sessionCount, sessionText, sessionUnavailableTopology, sessionCaseIds, validateSessionCohort } from './session-contract.mjs';
import { validateSessionProcesses } from './session-process-identity.mjs';

const envelopeFields = ['schemaVersion', 'sessionId', 'target', 'nodeCount', 'sourceRevision', 'runId', 'attempt',
  'jobId', 'repository', 'ref', 'workflow', 'images', 'processes', 'targetProfile', 'runtime', 'assemblies', 'cases', 'runtimeReport', 'resourceReport', 'outerRuntime', 'qualificationReport', 'outcome', 'reason'];
const caseFields = ['caseId', 'status', 'detail', 'initialRecords', 'expectedRecords', 'verifiedRecords', 'lanes', 'elapsedSeconds'];
const measurementFields = ['attempts', 'successes', 'failures', 'elapsedSeconds', 'usefulOperationsPerSecond',
  'latency', 'uniqueCompletedMessages', 'enqueue', 'receive', 'ack', 'clientResources'];
const finite = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const close = (left, right) => Math.abs(left - right) <= Math.max(1, Math.abs(right)) * 1e-8;
const image = value => typeof value === 'string' && /(?:@sha256:|^sha256:)[a-f0-9]{64}$/.test(value);
// Every target, including KeyLoad, uses an original released image pinned to its registry digest.
const keyLoadImage = value => typeof value === 'string'
  && /^ghcr\.io\/managedcode\/keyload:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}@sha256:[a-f0-9]{64}$/.test(value);
const noApplicableModels = (target, cases) => ['Qdrant', 'RabbitMQ', 'KurrentDB'].includes(target)
  && !cases.some(id => ['model-prepare', 'vector-exact', 'queue-cycle', 'stream-append', 'stream-read', 'stream-mixed'].includes(id));
const runtimeNames = Object.freeze({ 'load-million': 'LoadMillion', index: 'BuildIndexes',
  'sequential-read': 'SequentialRead', 'random-read': 'RandomRead', search: 'Search', mixed: 'Mixed',
  'verify-final': 'VerifyFinalState', 'model-prepare': 'PrepareModels', 'vector-exact': 'VectorExact', 'queue-cycle': 'QueueCycle',
  'stream-append': 'StreamAppend', 'stream-read': 'StreamRead', 'stream-mixed': 'StreamMixed' });
const modelCases = ['vector-exact', 'queue-cycle', 'stream-append', 'stream-read', 'stream-mixed'];

export function validateSessionReference(value) {
  require(exact(value, ['path', 'sha256']) && sessionPath(value.path) && sessionHash(value.sha256));
  return value;
}
function latency(value) {
  require(exact(value, ['p50Ms', 'p95Ms', 'p99Ms']) && Object.values(value).every(finite)
    && value.p50Ms <= value.p95Ms && value.p95Ms <= value.p99Ms);
}
export function validateSessionMeasurement(value) {
  require(exact(value, measurementFields) && [value.attempts, value.successes, value.failures, value.uniqueCompletedMessages].every(sessionCount)
    && value.attempts === value.successes + value.failures && finite(value.elapsedSeconds) && value.elapsedSeconds > 0
    && finite(value.usefulOperationsPerSecond) && close(value.usefulOperationsPerSecond, value.successes / value.elapsedSeconds));
  latency(value.latency);
  for (const key of ['enqueue', 'receive', 'ack']) if (value[key] !== null) latency(value[key]);
  if (value.clientResources !== null) {
    require(exact(value.clientResources, ['cpuSeconds', 'allocatedBytes', 'peakObservedWorkingSetBytes', 'samplingIntervalMs'])
      && finite(value.clientResources.cpuSeconds) && sessionCount(value.clientResources.allocatedBytes)
      && sessionCount(value.clientResources.peakObservedWorkingSetBytes) && sessionPositive(value.clientResources.samplingIntervalMs));
  }
  return value;
}
export function validateSessionSamples(lane, maximum) {
  const samples = lane.samples;
  require(Array.isArray(samples) && samples.length === lane.measurement.attempts && samples.length <= maximum);
  const ordinals = new Set();
  const durations = new Float64Array(samples.length);
  let succeeded = 0;
  for (const [index, sample] of samples.entries()) {
    require(exact(sample, ['operation', 'worker', 'startedMs', 'completedMs', 'success', 'error'])
      && sessionCount(sample.operation) && sample.operation < maximum && !ordinals.has(sample.operation)
      && sessionCount(sample.worker) && sample.worker < SESSION.concurrency && finite(sample.startedMs)
      && finite(sample.completedMs) && sample.startedMs <= sample.completedMs
      && sample.startedMs >= lane.firstStartedMs && sample.completedMs <= lane.lastCompletedMs
      && typeof sample.success === 'boolean' && (sample.success ? sample.error === null : sessionText(sample.error)));
    ordinals.add(sample.operation);
    durations[index] = sample.completedMs - sample.startedMs;
    if (sample.success) succeeded += 1;
  }
  require(succeeded === lane.measurement.successes);
  if (durations.length === 0) require(Object.values(lane.measurement.latency).every(value => value === 0));
  else {
    durations.sort();
    for (const [field, fraction] of [['p50Ms', 0.5], ['p95Ms', 0.95], ['p99Ms', 0.99]])
      require(close(lane.measurement.latency[field], durations[Math.max(0, Math.ceil(fraction * durations.length) - 1)]));
  }
  return lane;
}
export function validateSessionCase(value, id, original = true) {
  const model = modelCases.includes(id);
  const initial = ['load-million', 'queue-cycle', 'model-prepare'].includes(id) ? 0
    : SESSION.records + (['stream-read', 'stream-mixed'].includes(id) ? SESSION.operations : 0);
  const expected = id === 'queue-cycle' ? 0 : SESSION.records
    + (['stream-append', 'stream-read'].includes(id) ? SESSION.operations : id === 'stream-mixed' ? 2 * SESSION.operations : 0);
  require(exact(value, caseFields) && value.caseId === id && SESSION.caseIds.includes(id)
    && ['measured', 'failed', 'unavailable', 'notQualified'].includes(value.status)
    && [value.initialRecords, value.expectedRecords, value.verifiedRecords].every(sessionCount)
    && (value.status === 'measured' ? value.initialRecords === initial
      && (id === 'model-prepare' ? [0, SESSION.records].includes(value.expectedRecords) : value.expectedRecords === expected)
      : (value.initialRecords === 0 || value.initialRecords === SESSION.records) && value.expectedRecords === SESSION.records)
    && finite(value.elapsedSeconds) && Array.isArray(value.lanes) && value.lanes.length <= 32);
  require(value.status === 'measured' ? value.detail === null && value.verifiedRecords === (model ? SESSION.operations
    : ['load-million', 'mixed', 'verify-final'].includes(id) ? SESSION.records : 0)
    : sessionText(value.detail));
  if (value.status === 'unavailable') require(value.lanes.length === 0 && value.verifiedRecords === 0 && !/^AdapterSession|^LiveSessionPrerequisite|^SessionLoadAlreadyAttempted|SessionRequires/.test(value.detail));
  const lanes = new Set();
  for (const lane of value.lanes) {
    require(exact(lane, original ? ['id', 'measurement', 'firstStartedMs', 'lastCompletedMs', 'samples']
      : ['id', 'measurement', 'firstStartedMs', 'lastCompletedMs']) && sessionText(lane.id)
      && !lanes.has(lane.id) && finite(lane.firstStartedMs) && finite(lane.lastCompletedMs)
      && lane.firstStartedMs <= lane.lastCompletedMs && lane.lastCompletedMs <= value.elapsedSeconds * 1000 + 1);
    lanes.add(lane.id);
    validateSessionMeasurement(lane.measurement);
    if (original) {
      validateSessionSamples(lane, id === 'load-million' ? SESSION.records : SESSION.operations);
      if (value.status === 'measured') {
        const clients = id === 'stream-mixed' ? SESSION.concurrency / 2
          : id === 'mixed' ? lane.id === 'create' ? 4 : 3 : SESSION.concurrency;
        const actual = new Set(lane.samples.map(sample => sample.worker));
        require(actual.size === clients && lane.samples.every(sample => sample.worker < clients));
      }
    }
    if (value.status === 'measured') require(lane.measurement.failures === 0
      && lane.measurement.successes >= (id === 'load-million' ? SESSION.records : SESSION.operations));
  }
  if (value.status !== 'measured') return value;
  require(value.elapsedSeconds > 0);
  if (id === 'load-million') require(value.lanes.length === 1 && lanes.has('create'));
  if (['sequential-read', 'random-read'].includes(id)) require(value.lanes.length === 1 && lanes.has('read'));
  if (id === 'search') require(value.lanes.length === 1 && lanes.has('search'));
  if (model && id !== 'stream-mixed') require(value.lanes.length === 1 && lanes.has(id));
  if (id === 'queue-cycle') require(value.lanes[0].measurement.uniqueCompletedMessages === SESSION.operations
    && ['enqueue', 'receive', 'ack'].every(key => value.lanes[0].measurement[key] !== null));
  if (id === 'stream-mixed') require(value.lanes.length === 2 && lanes.has('stream-append') && lanes.has('stream-read')
    && Math.max(...value.lanes.map(lane => lane.firstStartedMs)) < Math.min(...value.lanes.map(lane => lane.lastCompletedMs)));
  if (['index', 'verify-final', 'model-prepare'].includes(id)) require(value.lanes.length === 0);
  if (id === 'mixed') {
    require(value.lanes.length === 5);
    for (const lane of ['create', 'delete', 'update', 'read', 'search']) require(lanes.has(lane));
    const mutation = value.lanes.filter(lane => ['create', 'delete', 'update'].includes(lane.id));
    require(mutation.every(lane => lane.measurement.attempts === SESSION.operations)
      && Math.max(...value.lanes.map(lane => lane.firstStartedMs)) < Math.min(...value.lanes.map(lane => lane.lastCompletedMs)));
  }
  return value;
}
export function validateSessionEnvelope(value, row, cohort, jobId, runtimeIdentity = null, caseIds = SESSION.caseIds) {
  sessionCaseIds(caseIds);
  validateSessionCohort(cohort);
  require(exact(value, envelopeFields) && value.schemaVersion === SESSION.schema && value.target === row.target
    && value.nodeCount === row.nodeCount && value.jobId === jobId && sessionPositive(jobId)
    && value.sessionId === row.id
    && ['success', 'failure', 'unavailable'].includes(value.outcome));
  for (const key of ['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow']) require(value[key] === cohort[key]);
  require(value.outcome === 'success' ? value.reason === null : sessionText(value.reason));
  const topologyFailure = sessionUnavailableTopology(row) && value.outcome === 'failure'
    && value.reason === 'SessionTestFailure' && value.targetProfile === null;
  validateSessionProcesses(value.processes, runtimeIdentity);
  require(exact(value.images, ['server'])
    && (value.images.server === null ? value.targetProfile === null && (topologyFailure
      || value.outcome === 'unavailable' && value.reason === SESSION.topologyUnavailable && sessionUnavailableTopology(row))
      : image(value.images.server) && (row.target !== 'KeyLoad' || keyLoadImage(value.images.server))));
  require(exact(value.runtime, ['operatingSystem', 'architecture', 'logicalProcessors', 'framework', 'storage'])
    && /Linux|Ubuntu/i.test(value.runtime.operatingSystem ?? '') && sessionPositive(value.runtime.logicalProcessors)
    && ['architecture', 'framework', 'storage'].every(key => sessionText(value.runtime[key])));
  if (value.targetProfile === null) require(value.outcome === 'failure'
    || value.outcome === 'unavailable' && value.reason === SESSION.topologyUnavailable
      && value.images.server === null && sessionUnavailableTopology(row)
    || value.outcome === 'unavailable' && value.reason === SESSION.capabilitiesUnavailable
      && noApplicableModels(value.target, caseIds) && image(value.images.server));
  else {
    const profile = value.targetProfile;
    require(exact(profile, ['name', 'version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization', 'image', 'cluster'])
      && profile.name === row.target && profile.image === value.images.server && ['version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization'].every(key => sessionText(profile[key]))
      && exact(profile.cluster, ['nodes', 'dataCopies', 'state', 'observations']) && profile.cluster.nodes === row.nodeCount
      && sessionPositive(profile.cluster.dataCopies) && profile.cluster.dataCopies <= row.nodeCount
      && sessionText(profile.cluster.state) && Array.isArray(profile.cluster.observations)
      && profile.cluster.observations.length <= 128 && profile.cluster.observations.every(sessionText));
  }
  if (value.resourceReport === null) require(value.images.server === null && value.targetProfile === null
    && (topologyFailure || value.outcome === 'unavailable' && value.reason === SESSION.topologyUnavailable
      && sessionUnavailableTopology(row)));
  else require(validateSessionReference(value.resourceReport).path === 'session-resource-evidence.json'
    && image(value.images.server));
  require(Array.isArray(value.assemblies) && value.assemblies.length >= 2 && value.assemblies.length <= 128);
  const assemblies = new Set();
  for (const assembly of value.assemblies) {
    require(exact(assembly, ['path', 'sha256', 'pdbSha256']) && sessionPath(assembly.path)
      && assembly.path.endsWith('.dll') && !assemblies.has(assembly.path)
      && sessionHash(assembly.sha256) && sessionHash(assembly.pdbSha256));
    assemblies.add(assembly.path);
  }
  require(Array.isArray(value.cases) && value.cases.length === caseIds.length);
  for (const [index, record] of value.cases.entries()) {
    require(exact(record, ['id', 'path', 'sha256']) && record.id === caseIds[index]
      && record.path === `reports/${record.id}.json` && sessionHash(record.sha256));
  }
  require(validateSessionReference(value.qualificationReport).path === 'session-qualification.json');
  require(validateSessionReference(value.outerRuntime).path === 'runtime-import.json');
  validateSessionReference(value.runtimeReport);
  require(value.runtimeReport.path.startsWith('RuntimeTestResults/') && value.runtimeReport.path.endsWith('.trx'));
  return value;
}
// A missing observed profile cannot authorize any measurement, even when execution infrastructure exists.
export function validateSessionProfileAccounting(session, reports, execution, qualification) {
  const caseIds = sessionCaseIds(session.cases.map(record => record.id));
  if (session.targetProfile !== null) {
    if (reports.some(report => report.status === 'measured')) require(
      [session.targetProfile.version,session.targetProfile.topology,session.targetProfile.cluster.state]
        .every(value => !/unverified|unobserved|unknown|not recorded/i.test(value)));
    return session;
  }
  require(reports.length === caseIds.length
    && reports.every((report,index) => report.caseId === caseIds[index] && report.status !== 'measured'));
  if (session.outcome === 'failure') {
    if (sessionUnavailableTopology(session)) require(session.images.server === null && session.resourceReport === null
      && session.reason === 'SessionTestFailure' && qualification.runtimeExitCode !== 0
      && qualification.status === 'notExecuted' && qualification.reason === 'ClientFailed'
      && reports.every(report => report.status === 'unavailable' && report.lanes.length === 0
        && report.elapsedSeconds === 0 && report.verifiedRecords === 0)
      && execution.length === caseIds.length && execution.every(test => test.outcome === 'Passed'));
    else require(image(session.images.server) && session.resourceReport !== null
      && (qualification.runtimeExitCode !== 0 || qualification.status === 'failed'));
    return session;
  }
  require(session.outcome === 'unavailable'
    && reports.every(report => report.status === 'unavailable' && report.lanes.length === 0
      && report.elapsedSeconds === 0 && report.verifiedRecords === 0)
    && execution.length === caseIds.length && execution.every(test => test.outcome === 'Passed'));
  if (session.reason === SESSION.capabilitiesUnavailable) {
    require(noApplicableModels(session.target, caseIds) && image(session.images.server)
      && session.resourceReport !== null && qualification.status === 'passed'
      && reports.every(report => report.detail === "Outside this target's shared contract."));
    return session;
  }
  require(session.reason === SESSION.topologyUnavailable && sessionUnavailableTopology(session) && session.images.server === null
    && session.resourceReport === null && qualification.status === 'notApplicable');
  return session;
}
export function validateSessionResults(xml, reports, caseIds = SESSION.caseIds) {
  sessionCaseIds(caseIds);
  require(typeof xml === 'string' && xml.length > 0 && xml.length <= SESSION.runtimeBytesCore
    && !/<!DOCTYPE|<!ENTITY/i.test(xml));
  const results = [...xml.matchAll(/<(?:[A-Za-z_][\w.-]*:)?UnitTestResult\s+([^>]+)\/?\s*>/g)].map(match => {
    const attrs = new Map([...match[1].matchAll(/([A-Za-z_][\w:.-]*)="([^"<>]*)"/g)].map(item => [item[1], item[2]]));
    return { name: attrs.get('testName'), outcome: attrs.get('outcome') };
  });
  require(results.length === caseIds.length);
  for (const report of reports) {
    const matched = results.filter(result => result.name === runtimeNames[report.caseId]
      || result.name?.endsWith('.' + runtimeNames[report.caseId]));
    require(matched.length === 1 && ['Passed', 'Failed'].includes(matched[0].outcome)
      && (report.status === 'measured' ? matched[0].outcome === 'Passed' : true));
  }
  return results;
}

export function validateSessionQualification(value, session, execution) {
  require(exact(value, ['schemaVersion', 'sessionId', 'target', 'nodeCount', 'runtimeExitCode', 'status', 'reason', 'steps'])
    && value.schemaVersion === 1 && value.sessionId === session.sessionId && value.target === session.target
    && value.nodeCount === session.nodeCount && Number.isInteger(value.runtimeExitCode)
    && value.runtimeExitCode >= -2_147_483_648 && value.runtimeExitCode <= 2_147_483_647 && ['passed', 'failed', 'notExecuted', 'notApplicable'].includes(value.status)
    && Array.isArray(value.steps) && value.steps.length <= 2
    && (value.status === 'passed' ? value.reason === null : sessionText(value.reason)));
  require(value.runtimeExitCode === 0 ? value.status !== 'notExecuted' && execution.every(item => item.outcome === 'Passed')
    : value.status === 'notExecuted' && session.outcome === 'failure' && session.reason === 'SessionTestFailure');
  if (value.status === 'notApplicable') require(session.targetProfile === null && value.steps.length === 0 && value.reason === 'RuntimeTopologyUnavailable');
  else if (value.status === 'notExecuted') require(value.runtimeExitCode !== 0
    && value.steps.length === 0 && value.reason === 'ClientFailed');
  else {
    const actual = value.steps.slice(0, 1);
    require(value.steps.length === 1 && actual.every(step => exact(step, ['id', 'status', 'failureType'])
      && step.id === 'execution-correctness'
      && ['passed', 'failed', 'notApplicable'].includes(step.status)
      && (step.status === 'failed' ? sessionText(step.failureType) : step.failureType === null)));
    require(actual.every(step => step.status !== 'notApplicable'));
    require(value.status === (actual.some(step => step.status === 'failed') ? 'failed' : 'passed'));
  }
  if (value.status === 'failed') require(value.reason === 'SessionQualificationFailed'
    && session.outcome === 'failure' && session.reason === 'SessionQualificationFailed');
  return value;
}
