#!/usr/bin/env node
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import {
  assertSnapshotsUnchanged,
  benchmarkAssembly,
  captureReleaseInventory,
  captureSourceInventory,
  createOwnedOutputDirectory,
  repositoryRoot,
  writeOwnedJson
} from './sample-chunk-development-inventory.mjs';
import { runBenchmarkProcess } from './sample-chunk-development-process.mjs';
import {
  buildDevelopmentReceipt,
  validateBenchmarkReports,
  validateCorpusManifests
} from './sample-chunk-development-report.mjs';

const MANIFEST_DIRECTORY_VARIABLE = 'KEYLOAD_CHUNK_BENCHMARK_MANIFEST_DIRECTORY';
const SOURCE_HEAD_VARIABLE = 'KEYLOAD_CHUNK_SOURCE_HEAD';
const SOURCE_INVENTORY_VARIABLE = 'KEYLOAD_CHUNK_SOURCE_INVENTORY_SHA256';
const OUTPUT_DIRECTORY_ARGUMENT = '--output';
const DRY_ARGUMENT = '--dry';
const FILTER = '*SampleChunkSerializationBenchmarks*';

export function parseArguments(args) {
  let output;
  let dry = false;
  for (let index = 0; index < args.length; index++) {
    if (args[index] === OUTPUT_DIRECTORY_ARGUMENT && index + 1 < args.length && output === undefined) {
      output = args[++index];
    } else if (args[index] === DRY_ARGUMENT && !dry) {
      dry = true;
    } else {
      throw new Error(`Unsupported or duplicate argument: ${args[index]}`);
    }
  }
  if (output === undefined) throw new Error('Supply --output artifacts/qualification/sample-chunk-development-<unique>.');
  return { output, dry };
}

export async function runSampleChunkDevelopment(options, signal) {
  if (typeof options?.output !== 'string' || typeof options.dry !== 'boolean') {
    throw new Error('The sample-chunk runner requires a validated output path and explicit run mode.');
  }
  const outputDirectory = await createOwnedOutputDirectory(options.output);
  const bdnDirectory = path.join(outputDirectory, 'bdn');
  const corpusDirectory = path.join(outputDirectory, 'corpus');
  await Promise.all([mkdir(bdnDirectory, { mode: 0o700 }), mkdir(corpusDirectory, { mode: 0o700 })]);
  const sourceBefore = await captureSourceInventory();
  const binariesBefore = await captureReleaseInventory();
  await writeOwnedJson(outputDirectory, 'source-before.json', sourceBefore);
  await writeOwnedJson(outputDirectory, 'release-before.json', binariesBefore);
  const environment = childEnvironment(sourceBefore, corpusDirectory);
  const args = benchmarkArguments(options.dry, bdnDirectory);
  let processResult;
  let processFailure;
  try {
    processResult = await runBenchmarkProcess({ executable: 'dotnet', args,
      cwd: repositoryRoot, environment, outputDirectory, signal });
  } catch (error) {
    processFailure = error;
  }
  const sourceAfter = await captureSourceInventory();
  const binariesAfter = await captureReleaseInventory();
  await writeOwnedJson(outputDirectory, 'source-after.json', sourceAfter);
  await writeOwnedJson(outputDirectory, 'release-after.json', binariesAfter);
  let inventoryFailure;
  try {
    await assertSnapshotsUnchanged(sourceBefore, sourceAfter, 'Repository source');
    await assertSnapshotsUnchanged(binariesBefore, binariesAfter, 'Release benchmark dependencies');
  } catch (error) {
    inventoryFailure = error;
  }
  if (processFailure && inventoryFailure) throw new AggregateError([processFailure, inventoryFailure], 'The benchmark failed and its source or binary inventory changed.');
  if (processFailure) throw processFailure;
  if (inventoryFailure) throw inventoryFailure;
  const reports = await validateBenchmarkReports(bdnDirectory, options.dry ? 'dry' : 'default');
  const manifests = await validateCorpusManifests(corpusDirectory, sourceBefore.head, sourceBefore.sha256,
    reports.hostEnvironment);
  const receipt = buildDevelopmentReceipt({ mode: options.dry ? 'dry' : 'default', sourceBefore, sourceAfter,
    binariesBefore, binariesAfter, reports, manifests, processResult, benchmarkArguments: args,
    manifestEnvironment: { directory: corpusDirectory, sourceHead: sourceBefore.head,
      sourceInventorySha256: sourceBefore.sha256 } });
  const receiptFile = await writeOwnedJson(outputDirectory, 'receipt.json', receipt);
  return { outputDirectory, receiptFile, receipt };
}

function benchmarkArguments(dry, artifactDirectory) {
  const settings = dry
    ? ['--job', 'Dry', '--launchCount', '1', '--warmupCount', '0', '--iterationCount', '1', '--iterationTime', '1']
    : ['--launchCount', '1', '--warmupCount', '3', '--iterationCount', '8', '--iterationTime', '100'];
  return [benchmarkAssembly, '--exporters', 'fulljson', '--filter', FILTER, '--keepFiles', '--stopOnFirstError',
    '--outliers', 'DontRemove', ...settings, '--artifacts', artifactDirectory];
}

function childEnvironment(source, corpusDirectory) {
  return {
    ...process.env,
    [MANIFEST_DIRECTORY_VARIABLE]: corpusDirectory,
    [SOURCE_HEAD_VARIABLE]: source.head,
    [SOURCE_INVENTORY_VARIABLE]: source.sha256
  };
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const controller = new AbortController();
  const interrupt = () => controller.abort();
  process.once('SIGINT', interrupt);
  process.once('SIGTERM', interrupt);
  try {
    const result = await runSampleChunkDevelopment(options, controller.signal);
    process.stdout.write(`Validated local sample-chunk ${options.dry ? 'Dry' : 'default'} receipt: ${path.join(result.outputDirectory, 'receipt.json')}\n`);
  } finally {
    process.removeListener('SIGINT', interrupt);
    process.removeListener('SIGTERM', interrupt);
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  main().catch((error) => {
    process.stderr.write(`${error?.stack ?? error}\n`);
    process.exitCode = 1;
  });
}
