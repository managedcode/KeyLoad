import { mkdir, readdir, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';
import { EXECUTION, PROFILE, positiveInteger, runtimeRequire } from './serialization-contract.mjs';
import { captureExecution, requireSameExecution } from './serialization-capture.mjs';
import { captureCorpus } from './serialization-corpus.mjs';
import { captureGenerated, generatedDirectories } from './serialization-generated.mjs';
import { executor, hostFacts } from './serialization-github.mjs';
import { collectFacts, hashObject, ownedOutput, readBounded, walkFiles, writeJson } from './serialization-files.mjs';
import { captureReadyGitHub } from './serialization-readiness.mjs';
import { validateSerializationReports } from './serialization-report.mjs';

async function prepare(directory, identity, environment) {
  await mkdir(directory, { recursive: true });
  runtimeRequire((await readdir(directory)).length === 0, 'output.fresh');
  await mkdir(join(directory, 'corpus'));
  const execution = await captureExecution(environment);
  const github = await captureReadyGitHub(directory, identity);
  await writeFile(join(directory, 'dotnet-info.txt'), execution.environment.dotnetInfo, { flag: 'wx' });
  await writeJson(join(directory, 'execution.json'), { schema: EXECUTION.schema, executor: { ...identity, jobId: github.job.id },
    profile: PROFILE, execution, host: hostFacts(), previousGenerated: await generatedDirectories(),
    capturedAt: new Date().toISOString() });
}

async function reports(directory) {
  const results = join(directory, 'results');
  const files = await readdir(results, { withFileTypes: true });
  runtimeRequire(files.every(file => file.isFile()), 'results.files');
  const full = files.filter(file => file.name.endsWith('-report-full.json'));
  const csv = files.filter(file => file.name.endsWith('-report.csv'));
  runtimeRequire(full.length === 3 && csv.length === 3, 'results.originals');
  runtimeRequire(full.every(file => csv.some(item => item.name === file.name.replace('-report-full.json', '-report.csv'))), 'results.pairs');
  const values = [];
  for (const file of full) values.push(JSON.parse((await readBounded(join(results, file.name))).toString('utf8')));
  return validateSerializationReports(values);
}

async function verify(directory, identity, environment) {
  const metadata = JSON.parse((await readBounded(join(directory, 'execution.json'), EXECUTION.fileBytes)).toString('utf8'));
  runtimeRequire(positiveInteger(metadata.executor?.jobId) && metadata.schema === EXECUTION.schema
    && JSON.stringify(metadata.profile) === JSON.stringify(PROFILE)
    && JSON.stringify(metadata.executor) === JSON.stringify({ ...identity, jobId: metadata.executor.jobId }), 'execution.identity');
  const github = await captureReadyGitHub(directory, identity, true, metadata.executor?.jobId);
  runtimeRequire(metadata.schema === EXECUTION.schema && JSON.stringify(metadata.profile) === JSON.stringify(PROFILE)
    && JSON.stringify(metadata.executor) === JSON.stringify({ ...identity, jobId: github.job.id }), 'execution.identity');
  requireSameExecution(metadata.execution, await captureExecution(environment));
  const qualification = await reports(directory);
  runtimeRequire(qualification.hostEnvironment.Architecture.toLowerCase() === metadata.host.architecture
    && qualification.hostEnvironment.LogicalCoreCount === metadata.host.logicalProcessors, 'Host.executor');
  const corpus = await captureCorpus(directory);
  const generated = await captureGenerated(directory, metadata.previousGenerated);
  runtimeRequire(generated.length > 0, 'generated.files');
  const paths = await walkFiles(directory);
  runtimeRequire(paths.includes('stdout.txt') && paths.includes('stderr.txt') && paths.includes('dotnet-info.txt')
    && paths.some(path => !path.includes('/') && path.endsWith('.log')), 'outputs.logs');
  const originalFiles = await collectFacts(directory, paths, ['stderr.txt']);
  await writeJson(join(directory, 'complete.json'), { schema: EXECUTION.completeSchema, executor: metadata.executor,
    profile: PROFILE, cellCount: qualification.cellCount, hostEnvironment: qualification.hostEnvironment,
    sourceSha256: metadata.execution.sources.sha256, environmentSha256: metadata.execution.environmentSha256,
    corpus, originalFiles, originalFilesSha256: hashObject(originalFiles), completedAt: new Date().toISOString(),
    qualification: 'Complete codec diagnostics; successful GitHub job and immutable artifact authentication remain required.' });
}

export async function serializationEvidence(argv = process.argv.slice(2), environment = process.env) {
  const { values } = parseArgs({ args: argv, options: { mode: { type: 'string' }, directory: { type: 'string' } },
    strict: true, allowPositionals: false });
  runtimeRequire(argv.length === 2 && ['prepare', 'verify'].includes(values.mode) && typeof values.directory === 'string', 'arguments');
  const identity = executor(environment);
  const directory = ownedOutput(environment, values.directory);
  if (values.mode === 'prepare') await prepare(directory, identity, environment);
  else await verify(directory, identity, environment);
  process.stdout.write(`Execution serialization evidence ${values.mode} completed.\n`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { await serializationEvidence(); }
  catch (error) {
    const message = error instanceof Error && error.message.startsWith('Invalid execution-serialization evidence: ')
      ? error.message : 'Execution serialization evidence failed.';
    process.stderr.write(`${message}\n`);
    process.exitCode = 1;
  }
}
