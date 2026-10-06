import { readIsolatedContract } from './isolated-plan.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { SCALED_PROFILES } from './scaled-isolated-plan.mjs';
import { AGGREGATE } from './aggregate-contracts.mjs';
import { validateServerResourceEvidence } from './server-resource-evidence.mjs';
import { CELL_TERMINAL, exactKeys, finite, lowercaseHash, nonnegative, positive, reject, safeCellId, sourceSha } from './open-loop-cell-terminal-contract.mjs';

const MEASUREMENT_KEYS = Object.freeze(['version', 'runId', 'attempt', 'jobId', 'startedAt', 'profileId', 'datasetRecords', 'offeredRatePerSecond', 'scenario', 'datasetSha256', 'target', 'sourceRevision', 'storage', 'elapsedSeconds', 'callerCancelled', 'drainExpired', 'scheduleComplete', 'mutationReadbackVerified', 'sessionsClosed', 'accounting', 'timing', 'samples', 'executionPolicy', 'worker', 'clientResources', 'runtime']);
const PROOF_KEYS = Object.freeze(['version', 'worker', 'profileId', 'rate', 'scenario', 'datasetRecords', 'datasetSha256', 'milestone', 'accounting', 'executionPolicy', 'callerCancelled', 'producerSettled', 'nativeCallsSettled', 'sessionsClosed', 'healthyReadVerified', 'healthyReadRevision', 'healthyReadSha256', 'healthyReadSessionClosed']);
const SIDECAR_KEYS = Object.freeze(['schema', 'artifactKind', 'artifactName', 'artifactSha256', 'offeredRatePerSecond', 'sourceRevision', 'runId', 'attempt', 'jobId', 'target', 'nodeCount', 'scenario', 'profile', 'hardware', 'appHostEnvelope', 'containers', 'missingEvidence', 'qualified', 'observationPolicy']);
const ACCOUNTING_KEYS = Object.freeze(['planned', 'notOffered', 'harnessRejected', 'timedOutBeforeStart', 'succeeded', 'failed', 'targetRejected', 'timedOutAfterStart', 'unfinishedQueued', 'unfinishedStarted', 'started', 'completed']);
const TIMING_KEYS = Object.freeze(['scheduledToTerminal', 'schedulerLag', 'queueDelay', 'serviceTime', 'successfulOperationsPerSecond', 'sampleCapacity', 'collectedSamples', 'missingSamples']);
const QUANTILE_KEYS = Object.freeze(['p50Milliseconds', 'p95Milliseconds', 'p99Milliseconds', 'sampleCount', 'missingSamples', 'denominator']);
const SAMPLE_KEYS = Object.freeze(['index', 'session', 'dueOffsetNanoseconds', 'decisionOffsetMilliseconds', 'offeredOffsetMilliseconds', 'startedOffsetMilliseconds', 'terminalOffsetMilliseconds', 'payloadBytes', 'outcome']);
const POLICY_KEYS = Object.freeze(['queueCapacity', 'concurrentSessions', 'maximumNodes', 'operationDeadlineMilliseconds', 'drainMilliseconds', 'controlPollMilliseconds', 'spinWindowMicroseconds']);
const TARGET_KEYS = Object.freeze(['name', 'version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization', 'image', 'cluster']);
const CLUSTER_KEYS = Object.freeze(['nodes', 'dataCopies', 'state', 'observations']);
const CLIENT_KEYS = Object.freeze(['cpuSeconds', 'allocatedBytes', 'peakObservedWorkingSetBytes', 'samplingIntervalMs']);
const OUTCOMES = Object.freeze(['NotOffered', 'HarnessRejected', 'TimedOutBeforeStart', 'Succeeded', 'Failed', 'TargetRejected', 'TimedOutAfterStart', 'UnfinishedQueued', 'UnfinishedStarted']);
const POLICY = Object.freeze({ queueCapacity: 64, concurrentSessions: 16, maximumNodes: 3, operationDeadlineMilliseconds: 30_000, drainMilliseconds: 30_000, controlPollMilliseconds: 100, spinWindowMicroseconds: 200 });

export function validateCellIdentity(planValue, cell) {
  const plan = validateOpenLoopPlan(planValue);
  return validateCanonicalCellIdentity(plan, cell);
}

export function validateCanonicalCellIdentity(plan, cell) {
  const canonical = [...plan.measurementCells, ...plan.cancellationProofCells].find(item => item.id === cell?.id);
  reject(canonical !== undefined && sameCell(canonical, cell));
  return canonical;
}

export function validateCellTerminal(value, planValue, expectedCell, expectedCohort = null) {
  const plan = validateOpenLoopPlan(planValue);
  return validateCellTerminalForPlan(value, plan, expectedCell, expectedCohort);
}

export function validateCellTerminalForPlan(value, plan, expectedCell, expectedCohort = null) {
  const cell = validateCanonicalCellIdentity(plan, expectedCell);
  reject(exactKeys(value, CELL_TERMINAL.terminalKeys) && value.schemaVersion === CELL_TERMINAL.version
    && value.kind === CELL_TERMINAL.kind && sameCell(value.cell, cell));
  validateWorker(value.worker, cell, expectedCohort);
  reject(['measured', 'cancellationProof', 'unsupportedTopology', 'failed'].includes(value.disposition));
  validateDisposition(value, cell);
  return value;
}

function validateDisposition(value, cell) {
  const unsupported = unsupportedReason(cell);
  if (value.disposition === 'failed') {
    reject(value.reason === CELL_TERMINAL.failedReason && Array.isArray(value.artifacts) && value.artifacts.length === 0);
    return;
  }
  if (value.disposition === 'unsupportedTopology') {
    reject(!cell.cancellationProof && unsupported !== null && value.reason === unsupported
      && Array.isArray(value.artifacts) && value.artifacts.length === 2);
    validateDescriptors(value.artifacts, [CELL_TERMINAL.genericSidecarFile, CELL_TERMINAL.genericWorkerFile]);
    return;
  }
  const expected = cell.cancellationProof ? 'cancellationProof' : 'measured';
  reject(unsupported === null && value.disposition === expected && value.reason === null && value.artifacts.length === 2);
  validateDescriptors(value.artifacts, cell.cancellationProof
    ? [CELL_TERMINAL.proofFile, CELL_TERMINAL.sidecarFile]
    : [CELL_TERMINAL.measurementFile, CELL_TERMINAL.sidecarFile]);
}

function validateDescriptors(items, names) {
  reject(Array.isArray(items) && items.length === names.length);
  const sorted = [...names].sort(ordinal);
  items.forEach((item, index) => reject(exactKeys(item, CELL_TERMINAL.descriptorKeys)
    && item.name === sorted[index] && positive(item.sizeInBytes)
    && item.sizeInBytes <= descriptorLimit(item.name) && lowercaseHash.test(item.sha256)));
}

function descriptorLimit(name) {
  if (name === CELL_TERMINAL.sidecarFile || name === CELL_TERMINAL.genericSidecarFile) return CELL_TERMINAL.sidecarBytes;
  return name === CELL_TERMINAL.genericWorkerFile ? AGGREGATE.workerBytes : CELL_TERMINAL.reportBytes;
}

function validateWorker(worker, cell, cohort) {
  reject(exactKeys(worker, CELL_TERMINAL.workerKeys) && safeCellId.test(cell.id)
    && worker.target === cell.target && worker.nodeCount === cell.nodeCount && worker.scenario === cell.scenario
    && worker.profile === cell.profile && sourceSha.test(worker.sourceRevision) && positive(worker.runId)
    && positive(worker.attempt) && worker.repository === CELL_TERMINAL.repository
    && worker.ref === CELL_TERMINAL.ref && worker.workflow === CELL_TERMINAL.workflow && positive(worker.jobId));
  if (cohort !== null) {
    reject(worker.sourceRevision === cohort.sourceRevision && worker.runId === cohort.runId
      && worker.attempt === cohort.attempt && worker.repository === cohort.repository
      && worker.ref === cohort.ref && worker.workflow === cohort.workflow);
  }
}

function validateMeasurement(value, cell, worker) {
  reject(exactKeys(value, MEASUREMENT_KEYS) && value.version === 1 && value.runId === worker.runId
    && value.attempt === worker.attempt && value.jobId === worker.jobId && validDate(value.startedAt)
    && value.profileId === cell.profile && value.datasetRecords === profileDocuments(cell.profile)
    && value.offeredRatePerSecond === cell.offeredRatePerSecond
    && value.scenario === cell.scenario && lowercaseHash.test(value.datasetSha256)
    && value.sourceRevision === worker.sourceRevision && typeof value.storage === 'string'
    && finite(value.elapsedSeconds) && value.elapsedSeconds > 0 && value.callerCancelled === false
    && value.drainExpired === false && value.scheduleComplete === true && value.sessionsClosed === true
    && value.mutationReadbackVerified === (cell.scenario !== 'PointRead') && validWorker(value.worker, worker));
  validateTarget(value.target, cell);
  validateAccounting(value.accounting, true);
  validateTiming(value.timing, value.samples, cell.profile, cell.offeredRatePerSecond, true);
  validatePolicy(value.executionPolicy);
  reject(typeof value.runtime === 'string' && value.runtime.trim().length > 0
    && value.runtime.length <= 256 && typeof value.storage === 'string' && value.storage.trim().length > 0
    && value.storage.length <= 512);
  validateClientResources(value.clientResources);
}

function validateProof(value, cell, worker) {
  reject(exactKeys(value, PROOF_KEYS) && value.version === 1 && validWorker(value.worker, worker)
    && value.profileId === cell.profile && value.rate === cell.offeredRatePerSecond
    && value.scenario === 'PointRead' && cell.scenario === 'PointRead' && cell.target === 'KeyLoad' && cell.nodeCount === 3
    && value.datasetRecords === profileDocuments(cell.profile) && lowercaseHash.test(value.datasetSha256)
    && value.callerCancelled === true && value.producerSettled === true && value.nativeCallsSettled === true
    && value.sessionsClosed === true && value.healthyReadVerified === true && positive(value.healthyReadRevision)
    && lowercaseHash.test(value.healthyReadSha256) && value.healthyReadSessionClosed === true);
  reject(exactKeys(value.milestone, ['completed', 'planned', 'started', 'offeredRatePerSecond', 'scenario'])
    && nonnegative(value.milestone.completed) && nonnegative(value.milestone.started)
    && value.milestone.scenario === cell.scenario
    && value.milestone.offeredRatePerSecond === cell.offeredRatePerSecond && value.milestone.completed >= 1_024
    && value.milestone.completed % 1_024 === 0 && value.milestone.planned === CELL_TERMINAL.planned
    && value.milestone.started >= value.milestone.completed && value.milestone.started <= value.milestone.planned);
  validateAccounting(value.accounting, false);
  reject(value.accounting.completed >= value.milestone.completed);
  validatePolicy(value.executionPolicy);
}

function validateAccounting(value, successful) {
  reject(exactKeys(value, ACCOUNTING_KEYS) && Object.values(value).every(nonnegative)
    && value.planned === CELL_TERMINAL.planned && value.completed <= value.started && value.started <= value.planned
    && value.notOffered + value.harnessRejected + value.timedOutBeforeStart + value.succeeded + value.failed
      + value.targetRejected + value.timedOutAfterStart + value.unfinishedQueued + value.unfinishedStarted === value.planned
    && value.started === value.succeeded + value.failed + value.targetRejected + value.timedOutAfterStart
      + value.unfinishedStarted
    && value.completed === value.succeeded + value.failed + value.targetRejected + value.timedOutAfterStart);
  if (successful) {
    reject(value.notOffered === 0 && value.harnessRejected === 0 && value.timedOutBeforeStart === 0
      && value.succeeded === value.planned && value.failed === 0 && value.targetRejected === 0
      && value.timedOutAfterStart === 0 && value.unfinishedQueued === 0 && value.unfinishedStarted === 0
      && value.started === value.planned && value.completed === value.planned);
  }
}

function validateTiming(value, samples, profileId, rate, successful) {
  reject(exactKeys(value, TIMING_KEYS) && Array.isArray(samples)
    && samples.length === CELL_TERMINAL.sampleCapacity && value.sampleCapacity === CELL_TERMINAL.sampleCapacity
    && value.collectedSamples === samples.length && value.missingSamples === 0
    && finite(value.successfulOperationsPerSecond) && value.successfulOperationsPerSecond > 0);
  for (const key of ['scheduledToTerminal', 'schedulerLag', 'queueDelay', 'serviceTime']) {
    const item = value[key];
    reject(exactKeys(item, QUANTILE_KEYS) && nonnegative(item.sampleCount) && nonnegative(item.missingSamples)
      && item.denominator === CELL_TERMINAL.sampleCapacity && item.sampleCount + item.missingSamples === item.denominator);
    if (successful) reject(item.sampleCount === CELL_TERMINAL.sampleCapacity && item.missingSamples === 0);
    validateQuantiles(item);
  }
  const payloadBytes = CELL_TERMINAL.payloadBytes;
  let previous = -1;
  samples.forEach((sample, slot) => {
    const expectedIndex = Math.floor(slot * (CELL_TERMINAL.planned - 1)
      / (CELL_TERMINAL.sampleCapacity - 1));
    reject(exactKeys(sample, SAMPLE_KEYS) && nonnegative(sample.index) && sample.index > previous
      && sample.index === expectedIndex && sample.index < CELL_TERMINAL.planned
      && (sample.session === null || nonnegative(sample.session))
      && sample.dueOffsetNanoseconds === Math.floor(sample.index * 1_000_000_000 / rate)
      && sample.payloadBytes === payloadBytes && OUTCOMES.includes(sample.outcome)
      && (!successful || sample.outcome === 'Succeeded'));
    validateSampleOffsets(sample);
    previous = sample.index;
  });
}

function validateSampleOffsets(sample) {
  const offsets = [sample.decisionOffsetMilliseconds, sample.offeredOffsetMilliseconds,
    sample.startedOffsetMilliseconds, sample.terminalOffsetMilliseconds];
  reject(offsets.every(item => item === null || finite(item) && item >= 0)
    && sample.terminalOffsetMilliseconds !== null);
  let previous = -1;
  for (const offset of offsets) {
    if (offset === null) continue;
    reject(offset >= previous);
    previous = offset;
  }
  const started = sample.outcome === 'Succeeded' || sample.outcome === 'Failed'
    || sample.outcome === 'TargetRejected' || sample.outcome === 'TimedOutAfterStart'
    || sample.outcome === 'UnfinishedStarted';
  if (sample.outcome === 'NotOffered') {
    reject(sample.session === null && sample.offeredOffsetMilliseconds === null
      && sample.startedOffsetMilliseconds === null);
  } else if (sample.outcome === 'HarnessRejected' || sample.outcome === 'TimedOutBeforeStart'
      || sample.outcome === 'UnfinishedQueued') {
    reject(sample.session === null && sample.offeredOffsetMilliseconds !== null
      && sample.startedOffsetMilliseconds === null);
  } else {
    reject(started && sample.session !== null && sample.offeredOffsetMilliseconds !== null
      && sample.startedOffsetMilliseconds !== null);
  }
}

function validateQuantiles(value) {
  const items = [value.p50Milliseconds, value.p95Milliseconds, value.p99Milliseconds];
  reject(items.every(item => item === null || finite(item) && item >= 0));
  if (value.sampleCount === 0) reject(items.every(item => item === null));
  if (value.sampleCount > 0) reject(items.every(item => item !== null)
    && value.p50Milliseconds <= value.p95Milliseconds && value.p95Milliseconds <= value.p99Milliseconds);
}

function validatePolicy(value) {
  reject(exactKeys(value, POLICY_KEYS) && Object.entries(POLICY).every(([key, expected]) => value[key] === expected));
}

function validateTarget(value, cell) {
  const descriptions = [value.version, value.topology, value.writeAcknowledgement,
    value.readContract, value.transport, value.authorization, value.cluster?.state];
  reject(exactKeys(value, TARGET_KEYS) && value.name === cell.target
    && descriptions.every(item => boundedText(item, 2_048))
    && Buffer.byteLength(descriptions.join(''), 'utf8') <= 8_192
    && (value.image === null || boundedText(value.image, 2_048))
    && exactKeys(value.cluster, CLUSTER_KEYS) && value.cluster.nodes === cell.nodeCount
    && positive(value.cluster.dataCopies) && typeof value.cluster.state === 'string'
    && Array.isArray(value.cluster.observations) && value.cluster.observations.length <= 64
    && value.cluster.observations.every(item => boundedText(item, 1_024))
    && value.cluster.observations.reduce((total, item) => total + Buffer.byteLength(item, 'utf8'), 0) <= 8_192);
}

function boundedText(value, maximumCharacters) {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= maximumCharacters;
}

function validateClientResources(value) {
  if (value === null) return;
  reject(exactKeys(value, CLIENT_KEYS) && finite(value.cpuSeconds) && value.cpuSeconds >= 0
    && nonnegative(value.allocatedBytes) && nonnegative(value.peakObservedWorkingSetBytes)
    && positive(value.samplingIntervalMs));
}

function validateSidecar(value, rawHash, sidecarHash, cell, worker, kind, artifactName) {
  reject(exactKeys(value, SIDECAR_KEYS) && value.schema === 'open-loop-server-resource-evidence.v1'
    && value.artifactKind === kind && value.artifactName === artifactName && value.artifactSha256 === rawHash
    && value.offeredRatePerSecond === cell.offeredRatePerSecond && value.sourceRevision === worker.sourceRevision
    && value.runId === worker.runId && value.attempt === worker.attempt && value.jobId === worker.jobId
    && value.target === cell.target && value.nodeCount === cell.nodeCount && value.scenario === cell.scenario
    && value.profile === cell.profile && value.qualified === true && Array.isArray(value.missingEvidence)
    && value.missingEvidence.length === 0 && Array.isArray(value.containers)
    && value.containers.length === cell.nodeCount && value.observationPolicy !== null
    && typeof value.observationPolicy === 'object' && !Array.isArray(value.observationPolicy));
  const mapped = { schema: 'server-resource-evidence.v2', sourceRevision: worker.sourceRevision,
    workflowRunId: String(worker.runId), runAttempt: String(worker.attempt), jobId: String(worker.jobId),
    target: value.target, nodeCount: value.nodeCount, scenario: value.scenario, profile: value.profile,
    workerSha256: value.artifactSha256, hardware: value.hardware, appHostEnvelope: value.appHostEnvelope,
    containers: value.containers, missingEvidence: value.missingEvidence, qualified: value.qualified,
    observationPolicy: value.observationPolicy };
  validateServerResourceEvidence(mapped, sidecarHash, rawHash, genericCell(cell), workerCohort(worker),
    worker.jobId, true);
}

function validWorker(actual, expected) {
  return exactKeys(actual, CELL_TERMINAL.workerKeys)
    && CELL_TERMINAL.workerKeys.every(key => actual[key] === expected[key]);
}

function unsupportedReason(cell) {
  const contract = readIsolatedContract();
  return contract.unsupportedTopologies.find(item => item.target === cell.target
    && item.nodeCounts.includes(cell.nodeCount))?.reason ?? null;
}

function workerCohort(worker) {
  return { sourceRevision: worker.sourceRevision, runId: worker.runId, attempt: worker.attempt,
    repository: worker.repository, ref: worker.ref, workflow: worker.workflow, profile: worker.profile };
}

function genericCell(cell) {
  return { id: cell.id, target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario,
    profile: cell.profile, family: 'scaled' };
}

function sameCell(left, right) {
  return exactKeys(left, CELL_TERMINAL.cellKeys) && exactKeys(right, CELL_TERMINAL.cellKeys)
    && CELL_TERMINAL.cellKeys.every(key => left[key] === right[key]);
}

function ordinal(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
function validDate(value) { return typeof value === 'string' && Number.isFinite(Date.parse(value)); }
function profileDocuments(id) {
  return SCALED_PROFILES.find(profile => profile.id === id)?.documents ?? -1;
}

export function validateNativeArtifact(value, cell, worker) {
  if (cell.cancellationProof) validateProof(value, cell, worker);
  else validateMeasurement(value, cell, worker);
}

export { validateSidecar as validateOpenLoopSidecar };
