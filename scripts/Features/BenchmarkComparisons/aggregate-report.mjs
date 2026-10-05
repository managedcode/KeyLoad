import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, KEYS, SUPPORT, VECTOR_SUPPORT, exactKeys, matches, positive, requireValue, text } from './aggregate-contracts.mjs';
import { validateMeasurement } from './sample-metrics.mjs';
import { scaleProfileSettings } from './scaled-isolated-plan.mjs';
import { vectorProfileSettings } from './vector-isolated-plan.mjs';

const ERROR = AGGREGATE.errors.report;
const PLATFORM_FIELDS = Object.freeze(['loadModel', 'hostOs', 'architecture', 'runtime', 'storage']);
const TARGET_TEXT_FIELDS = Object.freeze(['version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization']);
const DATE = /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)$/;
const SCALE_SAMPLE_CAPACITY = 4_096;
const SCALE_SAMPLE_ALGORITHM = 'evenly-spaced-operation-indices.v1';

function validateOptions(options, cell, contract) {
  requireValue(exactKeys(options, [...Object.keys(contract.options), 'topology']) &&
    options.topology === AGGREGATE.topology[cell.nodeCount] &&
    Object.entries(contract.options).every(([key, value]) => options[key] === value), ERROR);
}

function validateTarget(target, cell) {
  requireValue(exactKeys(target, KEYS.target) && target.name === cell.target &&
    TARGET_TEXT_FIELDS.every(field => text(target[field])) && matches(AGGREGATE.image, target.image), ERROR);
  const cluster = target.cluster;
  requireValue(exactKeys(cluster, KEYS.cluster) && cluster.nodes === cell.nodeCount && cluster.dataCopies === cell.nodeCount &&
    text(cluster.state) && Array.isArray(cluster.observations) && cluster.observations.length > 0 &&
    cluster.observations.every(text), ERROR);
}

function validateCase(item, cell, options, repetitions, scaled = false) {
  const fields = scaled ? KEYS.scaledCase : KEYS.case;
  const repeatCount = scaled ? 1 : options.repetitions;
  requireValue(exactKeys(item, fields) && item.target === cell.target && item.scenario === cell.scenario &&
    Number.isSafeInteger(item.repetition) && item.repetition >= 0 && item.repetition < repeatCount &&
    !repetitions.has(item.repetition), ERROR);
  repetitions.add(item.repetition);
  if (!SUPPORT[cell.target].includes(cell.scenario)) {
    requireValue(item.status === AGGREGATE.unsupported && text(item.detail) && item.measurement === null &&
      Array.isArray(item.samples) && item.samples.length === 0, ERROR);
    return;
  }
  requireValue(item.status === AGGREGATE.measured && item.detail === null, ERROR);
  if (scaled) validateScaledMeasuredCase(item, cell.scenario);
  else validateMeasuredCase(item, options, cell.scenario);
}

function validateVectorMetrics(metrics, profile) {
  requireValue(exactKeys(metrics, KEYS.vectorMetrics) && metrics.recordCount === profile.recordCount &&
    metrics.loadedRecordCount === profile.recordCount && metrics.queryAttempts === profile.measuredQueries &&
    metrics.querySuccesses === profile.measuredQueries && metrics.updateAttempts === profile.updateCount &&
    metrics.updateSuccesses === profile.updateCount && metrics.indexKind === profile.indexKind &&
    metrics.recallSamples === profile.measuredQueries && Array.isArray(metrics.perQueryRecall) &&
    metrics.perQueryRecall.length === profile.measuredQueries &&
    metrics.perQueryRecall.every(value => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1) &&
    Number.isFinite(metrics.exactRecall) && metrics.exactRecall >= 0 && metrics.exactRecall <= 1 &&
    Number.isFinite(metrics.minimumRecall) && metrics.minimumRecall >= 0 &&
    metrics.minimumRecall <= metrics.exactRecall && metrics.exactRecall >= profile.minimumRecall &&
    Number.isFinite(metrics.latencyP95Ms) && metrics.latencyP95Ms > 0 && Number.isFinite(metrics.latencyP99Ms) &&
    metrics.latencyP99Ms >= metrics.latencyP95Ms && Number.isFinite(metrics.indexBuildMilliseconds) &&
    metrics.indexBuildMilliseconds >= 0 && text(metrics.nativeIndexDefinition) && text(metrics.nativeQueryPlan) &&
    metrics.indexParameters !== null && typeof metrics.indexParameters === 'object' && !Array.isArray(metrics.indexParameters) &&
    Object.values(metrics.indexParameters).every(text) && metrics.serverMemoryBytes === null &&
    metrics.serverMemorySamplingIntervalMs === null && Number.isFinite(metrics.queryElapsedSeconds) &&
    metrics.queryElapsedSeconds > 0 && Number.isFinite(metrics.queryUsefulOperationsPerSecond) &&
    metrics.queryUsefulOperationsPerSecond > 0 && Number.isFinite(metrics.updateElapsedSeconds) &&
    metrics.updateElapsedSeconds >= 0 && Number.isFinite(metrics.updateUsefulOperationsPerSecond) &&
    metrics.updateUsefulOperationsPerSecond >= 0, ERROR);
  const average = metrics.perQueryRecall.reduce((total, value) => total + value, 0) / metrics.perQueryRecall.length;
  const minimum = metrics.perQueryRecall.reduce((previous, value) => Math.min(previous, value), 1);
  const close = (left, right) => Math.abs(left - right) <= Math.max(1e-8, Math.abs(right) * 1e-8);
  requireValue(close(metrics.exactRecall, average) && close(metrics.minimumRecall, minimum) &&
    close(metrics.queryUsefulOperationsPerSecond, profile.measuredQueries / metrics.queryElapsedSeconds), ERROR);
  if (profile.indexKind === 'Exact') requireValue(metrics.exactRecall === 1 && metrics.indexBuildMilliseconds === 0, ERROR);
  if (profile.indexKind === 'Hnsw') requireValue(/hnsw/iu.test(metrics.nativeIndexDefinition)
    && /hnsw/iu.test(metrics.nativeQueryPlan), ERROR);
  if (profile.indexKind === 'IvfFlat') requireValue(/ivfflat/iu.test(metrics.nativeIndexDefinition)
    && /ivfflat/iu.test(metrics.nativeQueryPlan), ERROR);
  if (profile.updateCount === 0) requireValue(metrics.updateElapsedSeconds === 0
    && metrics.updateUsefulOperationsPerSecond === 0, ERROR);
  else requireValue(metrics.updateElapsedSeconds > 0 && metrics.updateUsefulOperationsPerSecond > 0 &&
    close(metrics.updateUsefulOperationsPerSecond, profile.updateCount / metrics.updateElapsedSeconds), ERROR);
}

function validateVectorCase(item, cell, profile, repetitions) {
  requireValue(exactKeys(item, KEYS.vectorCase) && item.target === cell.target && item.scenario === 'VectorExact' &&
    item.repetition === 0 && !repetitions.has(item.repetition), ERROR);
  repetitions.add(item.repetition);
  if (!VECTOR_SUPPORT[cell.target].includes(profile.indexKind)) {
    requireValue(item.status === AGGREGATE.unsupported && text(item.detail) && item.measurement === null &&
      Array.isArray(item.samples) && item.samples.length === 0 && item.vectorMetrics === null, ERROR);
    return;
  }
  requireValue(item.status === AGGREGATE.measured && item.detail === null && item.measurement === null &&
    Array.isArray(item.samples) && item.samples.length === 0, ERROR);
  validateVectorMetrics(item.vectorMetrics, profile);
}

function validScaledSample(sample, index, scenario) {
  const operation = Math.floor(index * 99_999 / (SCALE_SAMPLE_CAPACITY - 1));
  return exactKeys(sample, KEYS.sample) && sample.operation === operation &&
    Number.isInteger(sample.worker) && sample.worker >= 0 && sample.worker < 16 &&
    Number.isFinite(sample.startedMs) && Number.isFinite(sample.completedMs) && sample.startedMs >= 0 &&
    sample.completedMs >= sample.startedMs && sample.success === true && sample.error === null &&
    sample.payloadBytes === 1_024 && sample.queue === null && sample.completedMessageId === null &&
    Number.isFinite(sample.latencyMs) && Math.abs(sample.latencyMs - (sample.completedMs - sample.startedMs)) <= 1e-8 &&
    scenario !== 'QueueCycle';
}

function validateScaledAccounting(value, sampleCount) {
  requireValue(exactKeys(value, KEYS.scaledCaseAccounting) && value.requested === 100_000 &&
    value.attempted === 100_000 && value.successes === 100_000 && value.failures === 0 &&
    value.deadlineTimeouts === 0 && value.rejections === 0 && value.unfinished === 0 &&
    value.samplingAlgorithm === SCALE_SAMPLE_ALGORITHM && value.sampleCapacity === SCALE_SAMPLE_CAPACITY &&
    value.collectedSamples === sampleCount && sampleCount === SCALE_SAMPLE_CAPACITY && value.missingSamples === 0 &&
    value.latencyQuantileMethod === 'sampled-estimate', ERROR);
}

function validateScaledMeasuredCase(item, scenario) {
  validateScaledAccounting(item.scaled, item.samples.length);
  requireValue(exactKeys(item.measurement, KEYS.measurement) && exactKeys(item.measurement.latency, KEYS.latency) &&
    item.samples.every((sample, index) => validScaledSample(sample, index, scenario)), ERROR);
  const measurement = item.measurement;
  const resources = measurement.clientResources;
  requireValue(measurement.attempts === 100_000 && measurement.successes === 100_000 && measurement.failures === 0 &&
    Number.isFinite(measurement.elapsedSeconds) && measurement.elapsedSeconds > 0 &&
    Number.isFinite(measurement.usefulOperationsPerSecond) && measurement.usefulOperationsPerSecond > 0 &&
    measurement.uniqueCompletedMessages === 0 && measurement.enqueue === null && measurement.receive === null &&
    measurement.ack === null && exactKeys(resources, ['cpuSeconds', 'allocatedBytes', 'peakObservedWorkingSetBytes', 'samplingIntervalMs']) &&
    Number.isFinite(resources.cpuSeconds) && resources.cpuSeconds >= 0 && Number.isSafeInteger(resources.allocatedBytes) &&
    resources.allocatedBytes >= 0 && Number.isSafeInteger(resources.peakObservedWorkingSetBytes) &&
    resources.peakObservedWorkingSetBytes >= 0 && Number.isSafeInteger(resources.samplingIntervalMs) &&
    resources.samplingIntervalMs > 0, ERROR);
  const maxCompletion = Math.max(...item.samples.map(sample => sample.completedMs));
  const latencies = item.samples.map(sample => sample.latencyMs).sort((left, right) => left - right);
  const percentile = fraction => latencies[Math.ceil(fraction * latencies.length) - 1];
  const close = (left, right) => Number.isFinite(left) && Math.abs(left - right) <= Math.max(1e-8, Math.abs(right) * 1e-8);
  requireValue(maxCompletion <= measurement.elapsedSeconds * 1_000 &&
    close(measurement.usefulOperationsPerSecond, 100_000 / measurement.elapsedSeconds) &&
    close(measurement.latency.p50Ms, percentile(0.50)) && close(measurement.latency.p95Ms, percentile(0.95)) &&
    close(measurement.latency.p99Ms, percentile(0.99)), ERROR);
}

function validateMeasuredCase(item, options, scenario) {
  requireValue(exactKeys(item.measurement, KEYS.measurement) && exactKeys(item.measurement.latency, KEYS.latency) &&
    Array.isArray(item.samples) && item.samples.length === options.operations, ERROR);
  requireValue(item.samples.every(sample => exactKeys(sample, KEYS.sample) &&
    (sample.queue === null || exactKeys(sample.queue, KEYS.queue))), ERROR);
  for (const stage of ['enqueue', 'receive', 'ack']) {
    requireValue(item.measurement[stage] === null || exactKeys(item.measurement[stage], KEYS.latency), ERROR);
  }
  const result = validateMeasurement(item.measurement, item.samples, options, scenario);
  requireValue(result !== null && result.successes === options.operations && result.failures === 0, ERROR);
}

export function validateReport(report, cell, cohort, contract) {
  const vector = cell.profile.startsWith('vector-');
  const scaled = !vector && cell.profile !== readControlProfile();
  const reportFields = vector ? KEYS.vectorReport : scaled ? KEYS.scaledReport : KEYS.report;
  requireValue(exactKeys(report, reportFields) && report.schemaVersion === AGGREGATE.reportVersion &&
    matches(AGGREGATE.guid, report.runId) && matches(DATE, report.startedAt) && Number.isFinite(Date.parse(report.startedAt)) &&
    report.sourceRevision === cohort.sourceRevision && matches(AGGREGATE.digest, report.datasetSha256) &&
    PLATFORM_FIELDS.every(field => text(report[field])) && matches(AGGREGATE.linux, report.hostOs) &&
    positive(report.logicalProcessors) && matches(AGGREGATE.image, report.loadGeneratorImage), ERROR);
  requireValue(exactKeys(report.provenance, KEYS.provenance) &&
    KEYS.provenance.every(key => report.provenance[key] === cohort[key]), ERROR);
  const vectorProfile = vector ? vectorProfileSettings(cell.profile) : null;
  if (vector) {
    requireValue(report.options === null && isDeepStrictEqual(report.vectorProfile, vectorProfile), ERROR);
  } else if (scaled) {
    requireValue(report.options === null && isDeepStrictEqual(report.scaledProfile, scaleProfileSettings(cell.profile)), ERROR);
  } else {
    validateOptions(report.options, cell, contract);
  }
  requireValue(Array.isArray(report.targets) && report.targets.length === 1, ERROR);
  validateTarget(report.targets[0], cell);
  const profileSettings = scaled ? scaleProfileSettings(cell.profile) : null;
  const repeatCount = vector ? vectorProfile.repetitions : scaled ? profileSettings.repetitions : contract.options.repetitions;
  requireValue(Array.isArray(report.cases) && report.cases.length === repeatCount, ERROR);
  const seenRepetitions = new Set();
  for (const item of report.cases) {
    if (vector) validateVectorCase(item, cell, vectorProfile, seenRepetitions);
    else validateCase(item, cell, scaled ? profileSettings : report.options, seenRepetitions, scaled);
  }
  return report;
}

function readControlProfile() {
  return 'intensive-1k-c16';
}
