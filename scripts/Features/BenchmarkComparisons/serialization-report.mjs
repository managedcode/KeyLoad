import { EXECUTION, expectedCells, positiveInteger, record, runtimeRequire } from './serialization-contract.mjs';
import { validateMeasurements, validateMemory, validateStatistics } from './serialization-measurements.mjs';

function validateHost(host) {
  runtimeRequire(record(host) && typeof host.BenchmarkDotNetVersion === 'string'
    && EXECUTION.version.test(host.BenchmarkDotNetVersion), 'Host.BenchmarkDotNetVersion');
  runtimeRequire(typeof host.RuntimeVersion === 'string' && EXECUTION.runtime.test(host.RuntimeVersion), 'Host.RuntimeVersion');
  runtimeRequire(typeof host.OsVersion === 'string' && EXECUTION.linux.test(host.OsVersion)
    && typeof host.ProcessorName === 'string' && host.ProcessorName.length > 0, 'Host.platform');
  runtimeRequire(positiveInteger(host.LogicalCoreCount) && ['X64', 'Arm64'].includes(host.Architecture)
    && host.Configuration === 'RELEASE' && host.HasAttachedDebugger === false
    && host.HasRyuJit === true && host.DotNetCliVersion === EXECUTION.sdk, 'Host.environment');
}

function validateJob(display, type, method, size) {
  runtimeRequire(typeof display === 'string' && display.length <= 4096, 'DisplayInfo');
  const prefix = `${type}.${method}: ${EXECUTION.jobId}(`;
  const position = display.indexOf(prefix);
  runtimeRequire(position === 0 || position > 0 && display.slice(0, position) === `${EXECUTION.namespace}.`, 'DisplayInfo.fixture');
  const propertiesStart = position + prefix.length;
  const end = display.indexOf(')', propertiesStart);
  runtimeRequire(end > propertiesStart && display.slice(end + 1).trim() === `[PayloadBytes=${size}]`, 'DisplayInfo.parameters');
  const properties = new Map();
  for (const pair of display.slice(propertiesStart, end).split(',')) {
    const parts = pair.trim().split('=');
    runtimeRequire(parts.length === 2 && !properties.has(parts[0]), 'DisplayInfo.settings');
    properties.set(parts[0], parts[1].trim());
  }
  runtimeRequire(properties.get('LaunchCount') === '2' && properties.get('WarmupCount') === '3'
    && properties.get('IterationCount') === '6', 'DisplayInfo.counts');
  const duration = /^(\d+(?:\.\d+)?)\s*(ms|s)$/.exec(properties.get('IterationTime') ?? '');
  runtimeRequire(duration !== null && Number(duration[1]) * (duration[2] === 's' ? 1000 : 1) === EXECUTION.iterationMs,
    'DisplayInfo.IterationTime');
  const allowed = ['LaunchCount', 'WarmupCount', 'IterationCount', 'IterationTime', 'RunStrategy', 'Runtime'];
  runtimeRequire([...properties.keys()].every(key => allowed.includes(key))
    && (!properties.has('RunStrategy') || properties.get('RunStrategy') === 'Throughput')
    && properties.get('Runtime') === '.NET 10.0', 'DisplayInfo.toolchain');
}

function validateBenchmark(benchmark) {
  runtimeRequire(record(benchmark) && benchmark.Namespace === EXECUTION.namespace && EXECUTION.types.includes(benchmark.Type), 'Benchmarks.type');
  runtimeRequire(EXECUTION.methods.includes(benchmark.Method), 'Benchmarks.method');
  runtimeRequire(typeof benchmark.Parameters === 'string' && /^PayloadBytes=(1024|16384)$/.test(benchmark.Parameters), 'Benchmarks.Parameters');
  const size = Number(benchmark.Parameters.slice('PayloadBytes='.length));
  validateJob(benchmark.DisplayInfo, benchmark.Type, benchmark.Method, size);
  const results = validateMeasurements(benchmark.Measurements);
  validateStatistics(benchmark.Statistics, results);
  validateMemory(benchmark.Memory);
  return `${benchmark.Type}/${size}/${benchmark.Method}`;
}

/** Validates original pinned BDN full-JSON reports; controlled inputs are never measurement evidence. */
export function validateSerializationReports(reports) {
  runtimeRequire(Array.isArray(reports) && reports.length === EXECUTION.types.length, 'reports');
  const cells = new Set();
  const types = new Set();
  let firstHost;
  for (const report of reports) {
    runtimeRequire(record(report), 'report');
    validateHost(report.HostEnvironmentInfo);
    firstHost ??= JSON.stringify(report.HostEnvironmentInfo);
    runtimeRequire(JSON.stringify(report.HostEnvironmentInfo) === firstHost, 'Host.cohort');
    runtimeRequire(Array.isArray(report.Benchmarks) && report.Benchmarks.length === 8, 'Benchmarks.count');
    const reportTypes = new Set(report.Benchmarks.map(benchmark => benchmark?.Type));
    runtimeRequire(reportTypes.size === 1 && !types.has([...reportTypes][0]), 'reports.type');
    types.add([...reportTypes][0]);
    for (const benchmark of report.Benchmarks) {
      const cell = validateBenchmark(benchmark);
      runtimeRequire(!cells.has(cell), 'Benchmarks.duplicate');
      cells.add(cell);
    }
  }
  runtimeRequire(cells.size === 24 && expectedCells().every(cell => cells.has(cell)), 'Benchmarks.complete');
  return { cellCount: cells.size, hostEnvironment: reports[0].HostEnvironmentInfo };
}
