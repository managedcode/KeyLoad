export const EXECUTION = Object.freeze({
  schema: 'keyload.execution-serialization', completeSchema: 'keyload.execution-serialization.complete',
  repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks',
  workflowPath: '.github/workflows/benchmarks.yml', job: 'execution-serialization', jobName: 'Measure execution serialization',
  namespace: 'KeyLoad.BenchmarkScenarios.BenchmarkComparisons', jobId: 'OrleansSerialization',
  sdk: '10.0.401',
  types: Object.freeze(['DocumentSerializationBenchmarks', 'CommandSerializationBenchmarks',
    'StorageSerializationBenchmarks']),
  methods: Object.freeze(['RuntimeEncode', 'RuntimeDecode', 'JsonEncode', 'JsonDecode']),
  sizes: Object.freeze([1024, 16384]), launches: 2, warmups: 3, iterations: 6, iterationMs: 200,
  output: 'artifacts/execution-serialization', bin: 'benchmarks/KeyLoad.Benchmarks/bin/Release/net10.0',
  reportBytes: 8 * 1024 * 1024, fileBytes: 64 * 1024 * 1024, totalBytes: 512 * 1024 * 1024,
  maximumFiles: 10000, maximumMeasurements: 4096,
  digest: /^[a-f0-9]{64}$/, sha: /^[a-f0-9]{40}$/, positiveId: /^[1-9][0-9]{0,19}$/,
  runtime: /^\.NET 10\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?(?: \([^()\r\n]+\))?$/,
  version: /^0\.15\.8(?:$|[+-][0-9A-Za-z.-]+)$/, linux: /(?:Linux|Ubuntu)/i,
});

export const PROFILE = Object.freeze({
  fixtures: EXECUTION.types, payloadBytes: EXECUTION.sizes, methods: EXECUTION.methods,
  launchCount: EXECUTION.launches, warmupCount: EXECUTION.warmups, iterationCount: EXECUTION.iterations,
  iterationTimeMs: EXECUTION.iterationMs, memoryDiagnoser: true, generatedExternalProcess: true,
  scope: 'Warm typed codec diagnostics; distinct execution and historical JSON validation/format contracts.',
  limits: 'No CLUSTER, database throughput, security equivalence, cold-start or website comparison claim.',
});

export function runtimeRequire(condition, field) {
  if (!condition) throw new Error(`Invalid execution-serialization evidence: ${field}.`);
}

export const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);
export const positive = value => typeof value === 'number' && Number.isFinite(value) && value > 0;
export const nonnegative = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
export const positiveInteger = value => Number.isSafeInteger(value) && value > 0;

export function exactKeys(value, keys) {
  return record(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

export function expectedCells() {
  return EXECUTION.types.flatMap(type => EXECUTION.sizes.flatMap(size =>
    EXECUTION.methods.map(method => `${type}/${size}/${method}`)));
}
