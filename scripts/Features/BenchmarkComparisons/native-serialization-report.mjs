import { NATIVE, expectedCells, positiveInteger, record, requireNative } from './native-serialization-contract.mjs';
import { validateMeasurements, validateMemory, validateStatistics } from './native-serialization-measurements.mjs';

function validateHost(host) {
  requireNative(record(host) && typeof host.BenchmarkDotNetVersion === 'string'
    && NATIVE.version.test(host.BenchmarkDotNetVersion), 'Host.BenchmarkDotNetVersion');
  requireNative(typeof host.RuntimeVersion === 'string' && NATIVE.runtime.test(host.RuntimeVersion), 'Host.RuntimeVersion');
  requireNative(typeof host.OsVersion === 'string' && NATIVE.linux.test(host.OsVersion)
    && typeof host.ProcessorName === 'string' && host.ProcessorName.length > 0, 'Host.platform');
  requireNative(positiveInteger(host.LogicalCoreCount) && ['X64', 'Arm64'].includes(host.Architecture)
    && host.Configuration === 'RELEASE' && host.HasAttachedDebugger === false
    && host.HasRyuJit === true && host.DotNetCliVersion === NATIVE.sdk, 'Host.environment');
}

function validateJob(display, type, method, size) {
  requireNative(typeof display === 'string' && display.length <= 4096, 'DisplayInfo');
  const prefix = `${type}.${method}: ${NATIVE.jobId}(`;
  const position = display.indexOf(prefix);
  requireNative(position === 0 || position > 0 && display.slice(0, position) === `${NATIVE.namespace}.`, 'DisplayInfo.fixture');
  const propertiesStart = position + prefix.length;
  const end = display.indexOf(')', propertiesStart);
  requireNative(end > propertiesStart && display.slice(end + 1).trim() === `[PayloadBytes=${size}]`, 'DisplayInfo.parameters');
  const properties = new Map();
  for (const pair of display.slice(propertiesStart, end).split(',')) {
    const parts = pair.trim().split('=');
    requireNative(parts.length === 2 && !properties.has(parts[0]), 'DisplayInfo.settings');
    properties.set(parts[0], parts[1].trim());
  }
  requireNative(properties.get('LaunchCount') === '2' && properties.get('WarmupCount') === '3'
    && properties.get('IterationCount') === '6', 'DisplayInfo.counts');
  const duration = /^(\d+(?:\.\d+)?)\s*(ms|s)$/.exec(properties.get('IterationTime') ?? '');
  requireNative(duration !== null && Number(duration[1]) * (duration[2] === 's' ? 1000 : 1) === NATIVE.iterationMs,
    'DisplayInfo.IterationTime');
  const allowed = ['LaunchCount', 'WarmupCount', 'IterationCount', 'IterationTime', 'RunStrategy', 'Runtime'];
  requireNative([...properties.keys()].every(key => allowed.includes(key))
    && (!properties.has('RunStrategy') || properties.get('RunStrategy') === 'Throughput')
    && properties.get('Runtime') === '.NET 10.0', 'DisplayInfo.toolchain');
}

function validateBenchmark(benchmark) {
  requireNative(record(benchmark) && benchmark.Namespace === NATIVE.namespace && NATIVE.types.includes(benchmark.Type), 'Benchmarks.type');
  requireNative(NATIVE.methods.includes(benchmark.Method), 'Benchmarks.method');
  requireNative(typeof benchmark.Parameters === 'string' && /^PayloadBytes=(1024|16384)$/.test(benchmark.Parameters), 'Benchmarks.Parameters');
  const size = Number(benchmark.Parameters.slice('PayloadBytes='.length));
  validateJob(benchmark.DisplayInfo, benchmark.Type, benchmark.Method, size);
  const results = validateMeasurements(benchmark.Measurements);
  validateStatistics(benchmark.Statistics, results);
  validateMemory(benchmark.Memory);
  return `${benchmark.Type}/${size}/${benchmark.Method}`;
}

/** Validates original pinned BDN full-JSON reports; controlled inputs are never measurement evidence. */
export function validateNativeSerializationReports(reports) {
  requireNative(Array.isArray(reports) && reports.length === NATIVE.types.length, 'reports');
  const cells = new Set();
  const types = new Set();
  let firstHost;
  for (const report of reports) {
    requireNative(record(report), 'report');
    validateHost(report.HostEnvironmentInfo);
    firstHost ??= JSON.stringify(report.HostEnvironmentInfo);
    requireNative(JSON.stringify(report.HostEnvironmentInfo) === firstHost, 'Host.cohort');
    requireNative(Array.isArray(report.Benchmarks) && report.Benchmarks.length === 8, 'Benchmarks.count');
    const reportTypes = new Set(report.Benchmarks.map(benchmark => benchmark?.Type));
    requireNative(reportTypes.size === 1 && !types.has([...reportTypes][0]), 'reports.type');
    types.add([...reportTypes][0]);
    for (const benchmark of report.Benchmarks) {
      const cell = validateBenchmark(benchmark);
      requireNative(!cells.has(cell), 'Benchmarks.duplicate');
      cells.add(cell);
    }
  }
  requireNative(cells.size === 24 && expectedCells().every(cell => cells.has(cell)), 'Benchmarks.complete');
  return { cellCount: cells.size, hostEnvironment: reports[0].HostEnvironmentInfo };
}
