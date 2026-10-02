import { javascriptType, queueStage, scenarioName, schemaField } from './contracts.mjs';

const percentileFields = Object.freeze({ [schemaField.p50Ms]: 0.5, [schemaField.p95Ms]: 0.95, [schemaField.p99Ms]: 0.99 });
const queueStages = Object.freeze({ [queueStage.enqueue]: schemaField.enqueueMs, [queueStage.receive]: schemaField.receiveMs, [queueStage.ack]: schemaField.ackMs });
const clientResourceFields = Object.freeze([schemaField.cpuSeconds, schemaField.allocatedBytes, schemaField.peakObservedWorkingSetBytes, schemaField.samplingIntervalMs]);
const toleranceScale = 1e-8;
const minimumTolerance = 1e-8;
const queueTimingRoundingToleranceMs = 0.0003;

export function isFiniteNumber(value) {
  return typeof value === javascriptType.number && Number.isFinite(value);
}

function equalNumber(actual, expected) {
  return isFiniteNumber(actual) && Math.abs(actual - expected) <= Math.max(minimumTolerance, Math.abs(expected) * toleranceScale);
}

function percentile(values, fraction) {
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.max(0, Math.ceil(fraction * sorted.length) - 1)];
}

function percentileSet(values) {
  return Object.fromEntries(Object.entries(percentileFields).map(([field, fraction]) => [field, percentile(values, fraction)]));
}

function validQueueTimings(value) {
  return value && Object.values(queueStages).every(field => isFiniteNumber(value[field]) && value[field] >= 0);
}

export function validateSample(sample, index, operations, concurrency, queueRequired, payloadBytes) {
  if (!sample || index < 0 || index >= operations || sample[schemaField.operation] !== index || !Number.isInteger(sample[schemaField.worker])
    || sample[schemaField.worker] < 0 || sample[schemaField.worker] >= concurrency || !isFiniteNumber(sample[schemaField.startedMs])
    || !isFiniteNumber(sample[schemaField.completedMs]) || sample[schemaField.startedMs] < 0 || sample[schemaField.completedMs] < sample[schemaField.startedMs]
    || !Number.isSafeInteger(sample[schemaField.payloadBytes]) || sample[schemaField.payloadBytes] !== payloadBytes || typeof sample[schemaField.success] !== javascriptType.boolean) return false;
  if (sample[schemaField.success] && sample[schemaField.error] !== null) return false;
  if (!sample[schemaField.success] && (typeof sample[schemaField.error] !== javascriptType.string || sample[schemaField.error].length === 0)) return false;
  if (queueRequired ? !validQueueTimings(sample[schemaField.queue]) : sample[schemaField.queue] !== null) return false;
  if (queueRequired ? typeof sample[schemaField.completedMessageId] !== javascriptType.string || sample[schemaField.completedMessageId].length === 0 : sample[schemaField.completedMessageId] !== null) return false;
  if (sample[schemaField.completedMessageId] !== null && typeof sample[schemaField.completedMessageId] !== javascriptType.string) return false;
  if (queueRequired && !queueStagesFitElapsed(sample)) return false;
  if (sample[schemaField.latencyMs] !== undefined && !equalNumber(sample[schemaField.latencyMs], sample[schemaField.completedMs] - sample[schemaField.startedMs])) return false;
  return true;
}

// Sample times are serialized to 0.0001 ms. Three stage values plus two interval endpoints can accumulate at most 0.00025 ms of rounding error.
function queueStagesFitElapsed(sample) {
  const queueTotal = Object.values(queueStages).reduce((sum, field) => sum + sample[schemaField.queue][field], 0);
  const elapsed = sample[schemaField.completedMs] - sample[schemaField.startedMs];
  return queueTotal <= elapsed + queueTimingRoundingToleranceMs;
}

function matchesPercentiles(actual, values) {
  if (!actual || values.length === 0) return false;
  const expected = percentileSet(values);
  return Object.keys(expected).every(field => equalNumber(actual[field], expected[field]));
}

function clientResourceMeasurementIsFinite(value) {
  if (!value || Object.keys(value).length !== clientResourceFields.length || clientResourceFields.some(field => !(field in value))) return false;
  return isFiniteNumber(value[schemaField.cpuSeconds]) && value[schemaField.cpuSeconds] >= 0
    && Number.isSafeInteger(value[schemaField.allocatedBytes]) && value[schemaField.allocatedBytes] >= 0
    && Number.isSafeInteger(value[schemaField.peakObservedWorkingSetBytes]) && value[schemaField.peakObservedWorkingSetBytes] >= 0
    && Number.isInteger(value[schemaField.samplingIntervalMs]) && value[schemaField.samplingIntervalMs] > 0;
}

export function validateMeasurement(measurement, samples, options, scenario) {
  if (!measurement || !Array.isArray(samples) || samples.length !== options[schemaField.operations]) return null;
  const queueRequired = scenario === scenarioName.queueCycle;
  if (samples.some((sample, index) => !validateSample(sample, index, options[schemaField.operations], options[schemaField.concurrency], queueRequired, options[schemaField.payloadBytes]))) return null;
  const latencies = samples.map(sample => sample[schemaField.completedMs] - sample[schemaField.startedMs]);
  const successes = samples.filter(sample => sample[schemaField.success]).length;
  const elapsedSeconds = measurement[schemaField.elapsedSeconds];
  const latestSampleCompletionSeconds = Math.max(...samples.map(sample => sample[schemaField.completedMs])) / 1000;
  if (!isFiniteNumber(elapsedSeconds) || elapsedSeconds <= 0 || latestSampleCompletionSeconds > elapsedSeconds) return null;
  const uniqueMessages = new Set(samples.filter(sample => sample[schemaField.success] && sample[schemaField.completedMessageId] !== null).map(sample => sample[schemaField.completedMessageId])).size;
  if (queueRequired && uniqueMessages !== samples.length) return null;
  const expectedCounts = measurement[schemaField.attempts] === samples.length && measurement[schemaField.successes] === successes
    && measurement[schemaField.failures] === samples.length - successes && equalNumber(measurement[schemaField.elapsedSeconds], elapsedSeconds)
    && equalNumber(measurement[schemaField.usefulOperationsPerSecond], successes / elapsedSeconds)
    && measurement[schemaField.uniqueCompletedMessages] === uniqueMessages;
  if (!expectedCounts || !matchesPercentiles(measurement[schemaField.latency], latencies) || !clientResourceMeasurementIsFinite(measurement[schemaField.clientResources])) return null;
  for (const [stage, field] of Object.entries(queueStages)) {
    const values = queueRequired ? samples.map(sample => sample[schemaField.queue][field]) : [];
    if (values.length > 0 ? !matchesPercentiles(measurement[stage], values) : measurement[stage] !== null) return null;
  }
  return { [schemaField.successes]: successes, [schemaField.failures]: samples.length - successes, [schemaField.elapsedSeconds]: elapsedSeconds,
    [schemaField.usefulOperationsPerSecond]: successes / elapsedSeconds, [schemaField.latency]: percentileSet(latencies) };
}
