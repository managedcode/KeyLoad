import { ISOLATED, WIRE, assertIsolated, exact, positive } from './isolated-contracts.mjs';

const finite = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;

function validateLatency(value) {
  assertIsolated(exact(value, WIRE.latency) && WIRE.latency.every(key => finite(value[key])) &&
    value.p50Ms <= value.p95Ms && value.p95Ms <= value.p99Ms);
}

export function validateCompactMeasurement(value, scenario) {
  assertIsolated(exact(value, WIRE.measurement) && value.attempts === ISOLATED.options.operations &&
    value.successes === value.attempts && value.failures === 0 && finite(value.elapsedSeconds) && value.elapsedSeconds > 0 &&
    finite(value.usefulOperationsPerSecond));
  const expected = value.successes / value.elapsedSeconds;
  assertIsolated(Number.isFinite(expected) && Math.abs(value.usefulOperationsPerSecond - expected) <=
    Math.max(ISOLATED.tolerance, Math.abs(expected) * ISOLATED.tolerance));
  validateLatency(value.latency);
  if (scenario === 'QueueCycle') {
    assertIsolated(value.uniqueCompletedMessages === value.successes);
    for (const stage of ['enqueue', 'receive', 'ack']) validateLatency(value[stage]);
  } else {
    assertIsolated(value.uniqueCompletedMessages === 0 && value.enqueue === null && value.receive === null && value.ack === null);
  }
  const resources = value.clientResources;
  assertIsolated(exact(resources, WIRE.resources) && finite(resources.cpuSeconds) &&
    Number.isSafeInteger(resources.allocatedBytes) && resources.allocatedBytes >= 0 &&
    Number.isSafeInteger(resources.peakObservedWorkingSetBytes) && resources.peakObservedWorkingSetBytes >= 0 &&
    positive(resources.samplingIntervalMs));
}
