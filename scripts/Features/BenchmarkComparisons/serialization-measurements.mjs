import { EXECUTION, nonnegative, positive, positiveInteger, record, runtimeRequire } from './serialization-contract.mjs';

function stageRows(measurements, stage, count, launch) {
  const rows = measurements.filter(row => row.IterationMode === 'Workload'
    && row.IterationStage === stage && row.LaunchIndex === launch);
  runtimeRequire(rows.length === count, `Measurements.${stage}`);
  const indexes = new Set(rows.map(row => row.IterationIndex));
  runtimeRequire(indexes.size === count && rows.every(row => positiveInteger(row.IterationIndex)
    && row.IterationIndex <= count && positive(row.Nanoseconds)), `Measurements.${stage}.IterationIndex`);
  return rows;
}

export function validateMeasurements(measurements) {
  runtimeRequire(Array.isArray(measurements) && measurements.length <= EXECUTION.maximumMeasurements, 'Measurements');
  const identities = new Set();
  for (const row of measurements) {
    runtimeRequire(record(row) && ['Workload', 'Overhead'].includes(row.IterationMode)
      && ['Jitting', 'Pilot', 'Warmup', 'Actual', 'Result'].includes(row.IterationStage), 'Measurements.mode');
    runtimeRequire(positiveInteger(row.LaunchIndex) && row.LaunchIndex <= EXECUTION.launches
      && Number.isSafeInteger(row.IterationIndex) && row.IterationIndex >= 0 && positiveInteger(row.Operations)
      && nonnegative(row.Nanoseconds), 'Measurements.values');
    const key = `${row.LaunchIndex}/${row.IterationMode}/${row.IterationStage}/${row.IterationIndex}`;
    runtimeRequire(!identities.has(key), 'Measurements.duplicate');
    identities.add(key);
  }
  const results = [];
  for (let launch = 1; launch <= EXECUTION.launches; launch++) {
    stageRows(measurements, 'Warmup', EXECUTION.warmups, launch);
    stageRows(measurements, 'Actual', EXECUTION.iterations, launch);
    const count = measurements.filter(row => row.IterationMode === 'Workload'
      && row.IterationStage === 'Result' && row.LaunchIndex === launch).length;
    runtimeRequire(count >= 1 && count <= EXECUTION.iterations, 'Measurements.Result');
    results.push(...stageRows(measurements, 'Result', count, launch));
  }
  runtimeRequire(results.every(row => positive(row.Nanoseconds)), 'Measurements.Result.Nanoseconds');
  return results;
}

export function validateStatistics(statistics, results) {
  runtimeRequire(record(statistics) && statistics.N === results.length, 'Statistics.N');
  const values = statistics.OriginalValues;
  runtimeRequire(Array.isArray(values) && values.length === statistics.N && values.every(positive), 'Statistics.OriginalValues');
  runtimeRequire(positive(statistics.Mean) && positive(statistics.Median)
    && nonnegative(statistics.StandardDeviation) && nonnegative(statistics.StandardError), 'Statistics.values');
  const actual = results.map(row => row.Nanoseconds / row.Operations).sort((left, right) => left - right);
  const declared = [...values].sort((left, right) => left - right);
  runtimeRequire(actual.every((value, index) => Math.abs(value - declared[index])
    <= Math.max(1, Math.abs(value)) * 1e-9), 'Statistics.measurementValues');
}

export function validateMemory(memory) {
  runtimeRequire(record(memory) && nonnegative(memory.BytesAllocatedPerOperation)
    && positiveInteger(memory.TotalOperations), 'Memory');
  runtimeRequire(['Gen0Collections', 'Gen1Collections', 'Gen2Collections'].every(key =>
    Number.isSafeInteger(memory[key]) && memory[key] >= 0), 'Memory.collections');
}
