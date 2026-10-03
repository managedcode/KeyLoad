import { createHash } from 'node:crypto';
import { captureOutputFiles, readBoundedRegularFile } from './sample-chunk-development-inventory.mjs';

export const CorpusNames = Object.freeze(['regular', 'late-equal', 'deterministic-random']);
export const RecordCounts = Object.freeze([1, 32, 256]);
export const BenchmarkMethods = Object.freeze(['NativeRecordsEncode', 'ChunkEncode', 'NativeRecordsDecode', 'ChunkDecode']);
const BENCHMARK_TYPE = 'SampleChunkSerializationBenchmarks';
const BENCHMARK_NAMESPACE = 'KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons';
const FIXTURE = 'SampleChunkSerializationBenchmarks';
const CORPUS_SEED = 78_031;
const CORPUS_GENERATOR = 'SHA256_seed_sample_lane_Int32LE';
const SHA256 = /^[0-9a-f]{64}$/;

export async function validateBenchmarkReports(artifactDirectory, mode) {
  requireMode(mode);
  const files = await captureOutputFiles(artifactDirectory, (name) => name.endsWith('-report-full.json'));
  if (files.length === 0) throw new Error('No original BenchmarkDotNet full JSON reports were produced.');
  const cases = new Map();
  const hostEnvironments = [];
  for (const file of files) {
    const bytes = await readBoundedRegularFile(artifactDirectory, file.path);
    if (sha256(bytes) !== file.sha256) throw new Error(`A report changed while validating: ${file.path}`);
    const report = parseJson(bytes, file.path);
    validateReportEnvelope(report, file.path);
    hostEnvironments.push(report.HostEnvironmentInfo);
    for (const item of report.Benchmarks) {
      const key = caseKey(item);
      if (cases.has(key)) throw new Error(`A benchmark case is duplicated: ${key}`);
      validateCase(item, mode);
      cases.set(key, summarizeCase(item));
    }
  }
  const expected = expectedCaseKeys();
  if (cases.size !== expected.length || expected.some((key) => !cases.has(key))) {
    throw new Error(`Expected exactly ${expected.length} unique chunk benchmark cases; found ${cases.size}.`);
  }
  assertSameHost(hostEnvironments);
  return { reportFiles: files, cases: [...cases.values()].sort(compareCases), hostEnvironment: hostEnvironments[0] };
}

export async function validateCorpusManifests(directory, sourceHead, sourceInventorySha256, hostEnvironment) {
  const files = await captureOutputFiles(directory, () => true);
  const expected = expectedManifestNames();
  if (files.length !== expected.length || expected.some((name) => !files.some((file) => file.path === name))) {
    throw new Error(`Expected exactly ${expected.length} corpus manifests.`);
  }
  const manifests = [];
  for (const file of files) {
    const bytes = await readBoundedRegularFile(directory, file.path);
    if (sha256(bytes) !== file.sha256) throw new Error(`A corpus manifest changed while validating: ${file.path}`);
    const manifest = parseJson(bytes, file.path);
    validateManifest(manifest, file.path, sourceHead, sourceInventorySha256);
    manifests.push(manifest);
  }
  assertSameManifestMachine(manifests, hostEnvironment);
  return { files, manifests };
}

export function buildDevelopmentReceipt({ mode, sourceBefore, sourceAfter, binariesBefore, binariesAfter,
  reports, manifests, processResult, benchmarkArguments, manifestEnvironment }) {
  requireMode(mode);
  validateReceiptInputs(sourceBefore, sourceAfter, binariesBefore, binariesAfter, reports, manifests,
    processResult, benchmarkArguments, manifestEnvironment);
  if (sourceBefore.sha256 !== sourceAfter.sha256 || sourceBefore.head !== sourceAfter.head
      || binariesBefore.sha256 !== binariesAfter.sha256) {
    throw new Error('A source or Release binary inventory changed before receipt creation.');
  }
  return {
    schemaVersion: 1,
    scope: 'local_sample_chunk_codec_development_control_only',
    runMode: mode,
    benchmarkArguments,
    manifestEnvironment,
    requestedSettings: mode === 'dry'
      ? { launchCount: 1, warmupCount: 0, iterationCount: 1, iterationTimeMilliseconds: 1, outliers: 'DontRemove' }
      : { launchCount: 1, warmupCount: 3, iterationCount: 8, iterationTimeMilliseconds: 100, outliers: 'DontRemove' },
    sourceHead: sourceBefore.head,
    sourceInventorySha256: sourceBefore.sha256,
    releaseBinaryInventorySha256: binariesBefore.sha256,
    sourceInventoryFile: 'source-before.json',
    releaseBinaryInventoryFile: 'release-before.json',
    unchangedAfterChild: true,
    benchmarkDotNetVersion: reports.hostEnvironment.BenchmarkDotNetVersion,
    hostEnvironment: reports.hostEnvironment,
    caseCount: reports.cases.length,
    cases: reports.cases,
    reportFiles: reports.reportFiles,
    corpusManifestCount: manifests.manifests.length,
    corpusManifests: manifests.files,
    standardOutput: processResult.stdout,
    standardError: processResult.stderr,
    childExitCode: processResult.exitCode,
    localDevelopmentOnly: true,
    performanceQualified: false,
    dryExecutionOnly: mode === 'dry',
    githubQualified: false,
    databaseScaleEvidence: false,
    canonicalChunkStorage: false,
    rewriteCostQualified: false,
    correctionRecoveryQualified: false
  };
}

function validateReceiptInputs(sourceBefore, sourceAfter, binariesBefore, binariesAfter, reports, manifests,
  processResult, benchmarkArguments, manifestEnvironment) {
  if (!/^[0-9a-f]{40}$/.test(sourceBefore?.head ?? '') || !SHA256.test(sourceBefore?.sha256 ?? '')
      || !SHA256.test(binariesBefore?.sha256 ?? '') || reports?.cases?.length !== 36
      || reports.reportFiles?.length < 1 || manifests?.manifests?.length !== 9
      || manifests.files?.length !== 9 || processResult?.exitCode !== 0
      || !Array.isArray(benchmarkArguments) || !benchmarkArguments.includes('--outliers')
      || benchmarkArguments[benchmarkArguments.indexOf('--outliers') + 1] !== 'DontRemove'
      || manifestEnvironment?.sourceHead !== sourceBefore.head
      || manifestEnvironment?.sourceInventorySha256 !== sourceBefore.sha256
      || !manifestEnvironment?.directory) {
    throw new Error('The development receipt inputs are incomplete or not source-bound.');
  }
  for (const log of [processResult.stdout, processResult.stderr]) {
    if (!log || typeof log.path !== 'string' || !Number.isInteger(log.bytes) || log.bytes < 0
        || log.bytes > 16 * 1024 * 1024 || !SHA256.test(log.sha256 ?? '') || log.truncated) {
      throw new Error('A bounded, complete original process log is required for the development receipt.');
    }
  }
}

function validateReportEnvelope(report, name) {
  if (!report || typeof report !== 'object' || !Array.isArray(report.Benchmarks)
      || !report.HostEnvironmentInfo || typeof report.HostEnvironmentInfo !== 'object') {
    throw new Error(`The BenchmarkDotNet 0.15.8 report envelope is malformed: ${name}`);
  }
  const host = report.HostEnvironmentInfo;
  for (const field of ['BenchmarkDotNetVersion', 'DotNetCliVersion', 'OsVersion', 'ProcessorName', 'RuntimeVersion', 'Architecture', 'Configuration']) {
    if (typeof host[field] !== 'string' || host[field].length === 0) {
      throw new Error(`BenchmarkDotNet host field ${field} is missing from ${name}.`);
    }
  }
  if (host.BenchmarkDotNetVersion !== '0.15.8' || host.DotNetCliVersion !== '10.0.401'
      || host.HasAttachedDebugger !== false || host.Configuration !== 'RELEASE'
      || !host.RuntimeVersion.startsWith('.NET 10.')) {
    throw new Error(`The benchmark report was not produced by the expected Release .NET 10 runtime: ${name}`);
  }
}

function validateCase(item, mode) {
  if (!item || item.Namespace !== BENCHMARK_NAMESPACE || item.Type !== BENCHMARK_TYPE
      || !BenchmarkMethods.includes(item.Method) || item.MethodTitle !== item.Method
      || typeof item.DisplayInfo !== 'string' || typeof item.Parameters !== 'string'
      || !item.Statistics || !item.Memory || !Array.isArray(item.Measurements) || !Array.isArray(item.Metrics)) {
    throw new Error('A BenchmarkDotNet case is missing its frozen type, method, parameters, or measurements.');
  }
  const params = parseParameters(item.Parameters);
  if (!RecordCounts.includes(params.RecordCount) || !CorpusNames.includes(params.Corpus)) {
    throw new Error(`Unexpected chunk benchmark parameters: ${item.Parameters}`);
  }
  validateDisplaySettings(item.DisplayInfo, mode);
  const results = item.Measurements.filter((measurement) => measurement.IterationMode === 'Workload'
    && measurement.IterationStage === 'Result');
  const expectedResults = mode === 'dry' ? 1 : 8;
  const expectedIterations = new Set(Array.from({ length: expectedResults }, (_, index) => index + 1));
  if (results.length !== expectedResults || results.some((measurement) => measurement.LaunchIndex !== 1
      || !Number.isInteger(measurement.IterationIndex) || !expectedIterations.has(measurement.IterationIndex)
      || measurement.Operations <= 0 || !Number.isInteger(measurement.Operations)
      || !Number.isFinite(measurement.Nanoseconds) || measurement.Nanoseconds <= 0)
      || new Set(results.map((measurement) => measurement.IterationIndex)).size !== expectedResults) {
    throw new Error(`The case ${item.Method}/${item.Parameters} has missing or invalid Workload/Result iterations.`);
  }
  const warmups = item.Measurements.filter((measurement) => measurement.IterationMode === 'Workload'
    && measurement.IterationStage === 'Warmup');
  if (warmups.length !== (mode === 'dry' ? 0 : 3) || warmups.some((measurement) => measurement.LaunchIndex !== 1
      || !Number.isInteger(measurement.Operations) || measurement.Operations <= 0
      || !Number.isFinite(measurement.Nanoseconds) || measurement.Nanoseconds <= 0)) {
    throw new Error(`The case ${item.Method}/${item.Parameters} has invalid workload warmups.`);
  }
  if (!Number.isFinite(item.Statistics.Mean) || item.Statistics.Mean <= 0
      || item.Statistics.N !== expectedResults || !Number.isFinite(item.Memory.BytesAllocatedPerOperation)
      || item.Memory.BytesAllocatedPerOperation < 0 || !Number.isFinite(item.Memory.TotalOperations)
      || item.Memory.TotalOperations <= 0 || !hasAllocatedMetric(item.Metrics)) {
    throw new Error(`The case ${item.Method}/${item.Parameters} lacks measured cost or allocation data.`);
  }
}

function validateDisplaySettings(displayInfo, mode) {
  const match = /\(([^()]*)\)/.exec(displayInfo);
  if (!match) throw new Error(`BenchmarkDotNet did not include actual job settings in: ${displayInfo}`);
  const settings = new Map(match[1].split(',').map((part) => {
    const delimiter = part.indexOf('=');
    return delimiter < 0 ? ['', ''] : [part.slice(0, delimiter).trim(), part.slice(delimiter + 1).trim()];
  }));
  const expected = mode === 'dry'
    ? { LaunchCount: '1', WarmupCount: '0', IterationCount: '1', IterationTime: '1ms' }
    : { LaunchCount: '1', WarmupCount: '3', IterationCount: '8', IterationTime: '100ms' };
  for (const [name, value] of Object.entries(expected)) {
    if (settings.get(name) !== value) throw new Error(`BenchmarkDotNet setting ${name} must be ${value}.`);
  }
}

function validateManifest(value, name, sourceHead, sourceInventorySha256) {
  const match = /^(1|32|256)-(regular|late-equal|deterministic-random)\.json$/.exec(name);
  if (!match) throw new Error(`Unexpected corpus manifest name: ${name}`);
  const count = Number(match[1]);
  const corpus = match[2];
  const expected = {
    schemaVersion: 1,
    scope: 'development_codec_microbenchmark_control_only',
    fixture: FIXTURE,
    sourceHead,
    sourceInventorySha256,
    corpus,
    seed: CORPUS_SEED,
    corpusGenerator: CORPUS_GENERATOR,
    actualRecordCount: count,
    correctness: 'every_field_original_offset_and_IEEE_bits_verified_before_timing',
    accounting: 'complete_native_value_envelopes_excluding_keys_WAL_replication_indexes_and_storage_overhead',
    databaseScaleEvidence: false,
    githubQualified: false,
    canonicalChunkStorage: false,
    rewriteCostQualified: false,
    correctionRecoveryQualified: false
  };
  for (const [key, expectedValue] of Object.entries(expected)) {
    if (value?.[key] !== expectedValue) throw new Error(`Corpus manifest ${name} has invalid ${key}.`);
  }
  for (const key of ['nativeValueBytes', 'chunkValueBytes', 'nativeValueBytesPerSample', 'chunkValueBytesPerSample']) {
    if (!Number.isFinite(value[key]) || value[key] <= 0) throw new Error(`Corpus manifest ${name} has invalid ${key}.`);
  }
  if (value.nativeValueBytesPerSample !== value.nativeValueBytes / count
      || value.chunkValueBytesPerSample !== value.chunkValueBytes / count
      || !SHA256.test(value.orderedNativeValuesSha256) || !SHA256.test(value.chunkValueSha256)) {
    throw new Error(`Corpus manifest ${name} has inconsistent byte counts or hashes.`);
  }
  if (typeof value.machineName !== 'string' || !value.machineName || !Number.isInteger(value.processorCount)
      || value.processorCount < 1 || typeof value.architecture !== 'string' || !value.architecture
      || typeof value.operatingSystem !== 'string' || !value.operatingSystem
      || typeof value.runtime !== 'string' || !value.runtime.startsWith('.NET 10.')) {
    throw new Error(`Corpus manifest ${name} lacks actual machine or runtime metadata.`);
  }
}

function assertSameHost(hosts) {
  const signature = JSON.stringify(hosts[0]);
  if (hosts.some((host) => JSON.stringify(host) !== signature)) {
    throw new Error('Benchmark reports came from different machine/runtime environments.');
  }
}

function assertSameManifestMachine(manifests, host) {
  const first = manifests[0];
  const machine = JSON.stringify([first.machineName, first.processorCount, first.architecture,
    first.operatingSystem, first.runtime]);
  if (!host.Architecture || manifests.some((item) => JSON.stringify([item.machineName, item.processorCount,
      item.architecture, item.operatingSystem, item.runtime]) !== machine
      || item.architecture !== host.Architecture || !host.RuntimeVersion.startsWith(item.runtime))) {
    throw new Error('Corpus manifests do not identify the report host consistently.');
  }
}

function hasAllocatedMetric(metrics) {
  return metrics.some((metric) => metric?.Descriptor?.Id === 'Allocated Memory'
    && Number.isFinite(metric.Value) && metric.Value >= 0);
}

function parseParameters(text) {
  const result = {};
  for (const part of text.split('&').map((value) => value.trim())) {
    const delimiter = part.indexOf('=');
    if (delimiter < 1) throw new Error(`Invalid chunk benchmark parameter: ${text}`);
    const key = part.slice(0, delimiter).trim();
    const value = part.slice(delimiter + 1).trim();
    if (key !== 'RecordCount' && key !== 'Corpus') {
      throw new Error(`Unknown chunk benchmark parameter: ${text}`);
    }
    if (Object.hasOwn(result, key)) throw new Error(`Duplicate chunk benchmark parameter: ${text}`);
    if (key === 'RecordCount') {
      const count = Number(value);
      if (!Number.isInteger(count) || String(count) !== value) {
        throw new Error(`The chunk benchmark record count is not canonical: ${text}`);
      }
      result[key] = count;
    } else {
      result[key] = value;
    }
  }
  if (Object.keys(result).length !== 2 || !Object.hasOwn(result, 'RecordCount') || !Object.hasOwn(result, 'Corpus')) {
    throw new Error(`The chunk benchmark parameters are incomplete: ${text}`);
  }
  return result;
}

function caseKey(item) {
  const parameters = parseParameters(item.Parameters);
  return `${item.Method}|${parameters.RecordCount}|${parameters.Corpus}`;
}

function expectedCaseKeys() {
  return RecordCounts.flatMap((count) => CorpusNames.flatMap((corpus) => BenchmarkMethods.map((method) =>
    `${method}|${count}|${corpus}`)));
}

function expectedManifestNames() {
  return RecordCounts.flatMap((count) => CorpusNames.map((corpus) => `${count}-${corpus}.json`));
}

function summarizeCase(item) {
  const parameters = parseParameters(item.Parameters);
  return { method: item.Method, recordCount: parameters.RecordCount, corpus: parameters.Corpus,
    displayInfo: item.DisplayInfo, meanNanoseconds: item.Statistics.Mean,
    bytesAllocatedPerOperation: item.Memory.BytesAllocatedPerOperation,
    workloadResultCount: item.Statistics.N };
}

function compareCases(left, right) {
  return left.recordCount - right.recordCount || left.corpus.localeCompare(right.corpus)
    || left.method.localeCompare(right.method);
}

function requireMode(mode) {
  if (mode !== 'default' && mode !== 'dry') throw new Error('The sample-chunk run mode is unknown.');
}

function parseJson(bytes, name) {
  try {
    return JSON.parse(bytes.toString('utf8'));
  } catch (error) {
    throw new Error(`Invalid JSON in ${name}.`, { cause: error });
  }
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}
