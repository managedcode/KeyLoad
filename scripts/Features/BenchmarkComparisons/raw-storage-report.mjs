const benchmarkNamespace = 'KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons';
const benchmarkType = 'RawStorageBenchmarks';
const benchmarkDotNetVersion = /^0\.15\.8(?:$|[+-][0-9A-Za-z.-]+)$/;
const runtimeVersion = /^\.NET 10\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?(?: \([^()\r\n]+\))?$/;
const methods = new Set(['PointRead', 'MissingRead', 'Overwrite', 'CreateDelete']);
const payloadSizes = new Set(['32', '1024']);
const recordCount = '4096';
const requiredParameterNames = ['Engine', 'PayloadBytes', 'RecordCount'];
const requiredActualRows = 5;
const maximumRetainedResultRows = 5;

function fail(field) {
  throw new Error(`Invalid raw-storage report: ${field}.`);
}

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isPositiveFinite(value) {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

function parseParameters(value) {
  if (typeof value !== 'string') fail('Parameters');
  const pairs = value.split('&');
  if (pairs.length !== requiredParameterNames.length) fail('Parameters');

  const parameters = Object.create(null);
  for (const pair of pairs) {
    const separator = pair.indexOf('=');
    if (separator <= 0 || separator !== pair.lastIndexOf('=') || separator === pair.length - 1) {
      fail('Parameters');
    }

    const name = pair.slice(0, separator);
    if (Object.hasOwn(parameters, name)) fail('Parameters');
    parameters[name] = pair.slice(separator + 1);
  }

  if (requiredParameterNames.some((name) => !Object.hasOwn(parameters, name))) fail('Parameters');
  return parameters;
}

function validateHost(report) {
  const host = report.HostEnvironmentInfo;
  if (!isRecord(host)) fail('HostEnvironmentInfo');
  if (typeof host.BenchmarkDotNetVersion !== 'string'
    || !benchmarkDotNetVersion.test(host.BenchmarkDotNetVersion)) fail('BenchmarkDotNetVersion');
  if (typeof host.RuntimeVersion !== 'string' || !runtimeVersion.test(host.RuntimeVersion)) {
    fail('RuntimeVersion');
  }
}

function validateStatistics(statistics) {
  if (!isRecord(statistics)) fail('Statistics');
  if (!Number.isInteger(statistics.N) || statistics.N < 1 || statistics.N > maximumRetainedResultRows) {
    fail('Statistics.N');
  }

  const originalValues = statistics.OriginalValues;
  if (!Array.isArray(originalValues) || originalValues.length !== statistics.N
    || !originalValues.every(isPositiveFinite)) fail('Statistics.OriginalValues');
  if (!isPositiveFinite(statistics.Mean)) fail('Statistics.Mean');
  if (!isPositiveFinite(statistics.Median)) fail('Statistics.Median');
}

function validateMemory(memory) {
  if (!isRecord(memory)) fail('Memory');
  const allocated = memory.BytesAllocatedPerOperation;
  if (typeof allocated !== 'number' || !Number.isFinite(allocated) || allocated < 0) {
    fail('Memory.BytesAllocatedPerOperation');
  }
  if (!isPositiveFinite(memory.TotalOperations)) fail('Memory.TotalOperations');
}

function isResultMeasurement(measurement) {
  return isRecord(measurement)
    && measurement.IterationMode === 'Workload'
    && measurement.IterationStage === 'Result';
}

function isActualMeasurement(measurement) {
  return isRecord(measurement)
    && measurement.IterationMode === 'Workload'
    && measurement.IterationStage === 'Actual';
}

function validateStageMeasurements(rows, launches, stage) {
  const iterations = new Set();
  for (const measurement of rows) {
    const launch = measurement.LaunchIndex;
    const iteration = measurement.IterationIndex;
    if (!Number.isInteger(launch) || launch < 0) fail('Measurements.LaunchIndex');
    if (!Number.isInteger(iteration) || iteration < 0 || iterations.has(iteration)) {
      fail(`Measurements.Workload.${stage}.IterationIndex`);
    }
    if (!isPositiveFinite(measurement.Operations)) fail('Measurements.Operations');
    if (!isPositiveFinite(measurement.Nanoseconds)) fail('Measurements.Nanoseconds');
    launches.add(launch);
    iterations.add(iteration);
  }
}

function validateMeasurements(measurements, retainedResultRows) {
  if (!Array.isArray(measurements)) fail('Measurements');
  const actual = measurements.filter(isActualMeasurement);
  const results = measurements.filter(isResultMeasurement);
  if (actual.length !== requiredActualRows) fail('Measurements.Workload.Actual');
  if (results.length !== retainedResultRows) fail('Measurements.Workload.Result');
  const launches = new Set();
  validateStageMeasurements(actual, launches, 'Actual');
  validateStageMeasurements(results, launches, 'Result');
  if (launches.size !== 1) fail('Measurements.LaunchIndex');
}

function validateBenchmark(benchmark, engine) {
  if (!isRecord(benchmark)) fail('Benchmarks[]');
  if (benchmark.Namespace !== benchmarkNamespace) fail('Namespace');
  if (benchmark.Type !== benchmarkType) fail('Type');
  if (typeof benchmark.Method !== 'string' || !methods.has(benchmark.Method)) fail('Method');

  const parameters = parseParameters(benchmark.Parameters);
  if (parameters.Engine !== engine) fail('Parameters.Engine');
  if (!payloadSizes.has(parameters.PayloadBytes)) fail('Parameters.PayloadBytes');
  if (parameters.RecordCount !== recordCount) fail('Parameters.RecordCount');
  validateStatistics(benchmark.Statistics);
  validateMemory(benchmark.Memory);
  validateMeasurements(benchmark.Measurements, benchmark.Statistics.N);
  return `${benchmark.Method}\u0000${parameters.PayloadBytes}`;
}

function validateCellSet(benchmarks, engine) {
  const actual = new Set();
  for (const benchmark of benchmarks) {
    const cell = validateBenchmark(benchmark, engine);
    if (actual.has(cell)) fail('duplicate cell');
    actual.add(cell);
  }

  for (const method of methods) {
    for (const payloadBytes of payloadSizes) {
      if (!actual.has(`${method}\u0000${payloadBytes}`)) fail('missing cell');
    }
  }
}

/** Validates one complete raw-storage BenchmarkDotNet full-JSON report without I/O or mutation. */
export function validateReport(report, engine) {
  if (engine !== 'zonetree') fail('engine');
  if (!isRecord(report)) fail('report');
  validateHost(report);
  if (!Array.isArray(report.Benchmarks) || report.Benchmarks.length !== methods.size * payloadSizes.size) {
    fail('Benchmarks');
  }

  validateCellSet(report.Benchmarks, engine);
}
