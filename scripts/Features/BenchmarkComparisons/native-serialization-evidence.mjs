import { mkdir, readdir, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';
import { NATIVE, PROFILE, requireNative } from './native-serialization-contract.mjs';
import { captureExecution, requireSameExecution } from './native-serialization-capture.mjs';
import { captureCorpus } from './native-serialization-corpus.mjs';
import { captureGenerated, generatedDirectories } from './native-serialization-generated.mjs';
import { captureGitHub, executor, hostFacts, requireCompletedSteps } from './native-serialization-github.mjs';
import { collectFacts, hashObject, ownedOutput, readBounded, walkFiles, writeJson } from './native-serialization-files.mjs';
import { validateNativeSerializationReports } from './native-serialization-report.mjs';

async function prepare(directory, identity, environment) {
  await mkdir(directory, { recursive: true });
  requireNative((await readdir(directory)).length === 0, 'output.fresh');
  await mkdir(join(directory, 'corpus'));
  const execution = await captureExecution(environment);
  const github = await captureGitHub(identity);
  requireCompletedSteps(github.job);
  await writeFile(join(directory, 'dotnet-info.txt'), execution.environment.dotnetInfo, { flag: 'wx' });
  await writeJson(join(directory, 'github-prepare.json'), github);
  await writeJson(join(directory, 'execution.json'), { schema: NATIVE.schema, executor: { ...identity, jobId: github.job.id },
    profile: PROFILE, execution, host: hostFacts(), previousGenerated: await generatedDirectories(),
    capturedAt: new Date().toISOString() });
}

async function reports(directory) {
  const results = join(directory, 'results');
  const files = await readdir(results, { withFileTypes: true });
  requireNative(files.every(file => file.isFile()), 'results.files');
  const full = files.filter(file => file.name.endsWith('-report-full.json'));
  const csv = files.filter(file => file.name.endsWith('-report.csv'));
  requireNative(full.length === 3 && csv.length === 3, 'results.originals');
  requireNative(full.every(file => csv.some(item => item.name === file.name.replace('-report-full.json', '-report.csv'))), 'results.pairs');
  const values = [];
  for (const file of full) values.push(JSON.parse((await readBounded(join(results, file.name))).toString('utf8')));
  return validateNativeSerializationReports(values);
}

async function verify(directory, identity, environment) {
  const metadata = JSON.parse((await readBounded(join(directory, 'execution.json'), NATIVE.fileBytes)).toString('utf8'));
  const github = await captureGitHub(identity);
  requireCompletedSteps(github.job, true);
  requireNative(metadata.schema === NATIVE.schema && JSON.stringify(metadata.profile) === JSON.stringify(PROFILE)
    && JSON.stringify(metadata.executor) === JSON.stringify({ ...identity, jobId: github.job.id }), 'execution.identity');
  requireSameExecution(metadata.execution, await captureExecution(environment));
  const qualification = await reports(directory);
  requireNative(qualification.hostEnvironment.Architecture.toLowerCase() === metadata.host.architecture
    && qualification.hostEnvironment.LogicalCoreCount === metadata.host.logicalProcessors, 'Host.executor');
  const corpus = await captureCorpus(directory);
  const generated = await captureGenerated(directory, metadata.previousGenerated);
  requireNative(generated.length > 0, 'generated.files');
  await writeJson(join(directory, 'github-verify.json'), github);
  const paths = await walkFiles(directory);
  requireNative(paths.includes('stdout.txt') && paths.includes('stderr.txt') && paths.includes('dotnet-info.txt')
    && paths.some(path => !path.includes('/') && path.endsWith('.log')), 'outputs.logs');
  const originalFiles = await collectFacts(directory, paths, ['stderr.txt']);
  await writeJson(join(directory, 'complete.json'), { schema: NATIVE.completeSchema, executor: metadata.executor,
    profile: PROFILE, cellCount: qualification.cellCount, hostEnvironment: qualification.hostEnvironment,
    sourceSha256: metadata.execution.sources.sha256, environmentSha256: metadata.execution.environmentSha256,
    corpus, originalFiles, originalFilesSha256: hashObject(originalFiles), completedAt: new Date().toISOString(),
    qualification: 'Complete codec diagnostics; successful GitHub job and immutable artifact authentication remain required.' });
}

export async function nativeSerializationEvidence(argv = process.argv.slice(2), environment = process.env) {
  const { values } = parseArgs({ args: argv, options: { mode: { type: 'string' }, directory: { type: 'string' } },
    strict: true, allowPositionals: false });
  requireNative(argv.length === 2 && ['prepare', 'verify'].includes(values.mode) && typeof values.directory === 'string', 'arguments');
  const identity = executor(environment);
  const directory = ownedOutput(environment, values.directory);
  if (values.mode === 'prepare') await prepare(directory, identity, environment);
  else await verify(directory, identity, environment);
  process.stdout.write(`Native serialization evidence ${values.mode} completed.\n`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { await nativeSerializationEvidence(); }
  catch (error) {
    const message = error instanceof Error && error.message.startsWith('Invalid native-serialization evidence: ')
      ? error.message : 'Native serialization evidence failed.';
    process.stderr.write(`${message}\n`);
    process.exitCode = 1;
  }
}
