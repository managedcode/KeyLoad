import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync } from 'node:fs';
import { availableParallelism } from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import { classifyClasses } from './ci-test-inventory.mjs';
import {
  assemblyFilter, checkPartition, findShard, laneFilter, loadManifest, namespaceFilter,
} from './ci-test-partition.mjs';
import { checkResults } from './ci-test-results.mjs';
import { checkCompiledInventory } from './ci-test-compiled-inventory.mjs';
import { executionBuildConfiguration, executionPipeDirectory } from './run-tests.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const usage = [
  'Usage: node scripts/Features/TestInfrastructure/ci-test-shards.mjs <command>',
  '  matrix <suite>                                   GitHub matrix JSON for functional or cluster',
  '  run <shard> [--results-directory <directory>]    run one shard (default results directory: TestResults)',
  '  verify [<results-directory>]                     exactly-once partition check, plus TRX identities when given',
  '  inventory <suite>                                source inventory with cluster use and lane assignment',
  '  verify-compiled <suite> <discovery.json>           compare original compiled discovery with source methods',
].join('\n');

function matrix(manifest, suiteId) {
  const suite = manifest.suites[suiteId];
  if (!suite) throw new Error(`Unknown suite: ${suiteId}`);
  const include = suite.shards.map(shard => (suite.partition === 'class'
    ? { shard: shard.id, name: shard.name, playwright: shard.playwright === true }
    : { shard: shard.id, name: shard.name }));
  return JSON.stringify({ include });
}

function testEnvironment() {
  // Share the selected configuration and bounded framework IPC directory with the direct runner.
  const pipes = executionPipeDirectory(root);
  mkdirSync(pipes, { recursive: true });
  return { ...process.env, TMPDIR: pipes, TMP: pipes, TEMP: pipes, TESTINGPLATFORM_PIPE_DIRECTORY: pipes };
}

function dotnetArguments(suite, run) {
  const parallelism = run.parallelism === 'processors' ? availableParallelism() : run.parallelism;
  return ['test', '--project', suite.project, '--configuration', executionBuildConfiguration(), '--no-build', '--output', 'Detailed', '--treenode-filter', run.filter,
    ...(parallelism ? ['--maximum-parallel-tests', String(parallelism)] : []),
    '--timeout', `${suite.timeoutMinutes}m`, '--report-trx', '--results-directory', run.directory];
}

function startRun(suite, run, environment, children) {
  mkdirSync(run.directory, { recursive: true });
  let processEnvironment = environment;
  if (suite.partition === 'class') {
    const diagnosticsDirectory = path.join(run.directory, 'dcp-diagnostics');
    mkdirSync(diagnosticsDirectory, { recursive: true });
    processEnvironment = { ...environment, DCP_DIAGNOSTICS_LOG_FOLDER: diagnosticsDirectory, DCP_DIAGNOSTICS_LOG_LEVEL: 'info' };
  }
  const started = Date.now();
  const args = dotnetArguments(suite, run);
  process.stdout.write(`[${run.label}] dotnet ${args.join(' ')}\n`);
  const child = spawn('dotnet', args, { cwd: root, env: processEnvironment, stdio: ['ignore', 'pipe', 'pipe'] });
  children.add(child);
  for (const [stream, target] of [[child.stdout, process.stdout], [child.stderr, process.stderr]]) {
    createInterface({ input: stream }).on('line', line => target.write(`[${run.label}] ${line}\n`));
  }
  return new Promise(resolve => {
    child.once('error', error => {
      process.stderr.write(`[${run.label}] ${error.message}\n`);
      resolve({ run, code: 1, seconds: 0 });
    });
    child.once('close', (code, signal) => {
      children.delete(child);
      resolve({ run, code: code ?? (signal ? 128 : 1), seconds: Math.round((Date.now() - started) / 1000) });
    });
  });
}

async function runShard(manifest, shardId, resultsRoot) {
  const { suiteId, suite, shard } = findShard(manifest, shardId);
  const base = path.resolve(root, resultsRoot, suiteId, shard.id);
  const environment = testEnvironment();
  const children = new Set();
  let interrupted = false;
  const forward = signal => () => { interrupted = true; for (const child of children) child.kill(signal); };
  const handlers = [['SIGINT', forward('SIGINT')], ['SIGTERM', forward('SIGTERM')]];
  for (const [signal, handler] of handlers) process.on(signal, handler);
  const outcomes = [];
  try {
    if (suite.partition === 'class') {
      // Lanes run concurrently; each lane runs one test at a time, so a runner holds one cluster per lane.
      const runs = shard.lanes.map(lane => ({
        label: `${shard.id}/${lane.id}`, filter: laneFilter(suite, lane), directory: path.join(base, lane.id),
        parallelism: lane.maximumParallelTests ?? suite.parallelism,
      }));
      outcomes.push(...await Promise.all(runs.map(run => startRun(suite, run, environment, children))));
    } else if (suite.partition === 'namespace') {
      // One assembly-pinned process per namespace keeps every filter segment literal.
      for (const namespace of shard.namespaces) {
        if (interrupted) break;
        const run = { label: `${shard.id}/${namespace}`, filter: namespaceFilter(suite, namespace), directory: path.join(base, namespace), parallelism: suite.parallelism };
        outcomes.push(await startRun(suite, run, environment, children));
      }
    } else {
      const run = { label: shard.id, filter: assemblyFilter(suite), directory: base, parallelism: suite.parallelism };
      outcomes.push(await startRun(suite, run, environment, children));
    }
  } finally {
    for (const [signal, handler] of handlers) process.off(signal, handler);
  }
  for (const { run, code, seconds } of outcomes) process.stdout.write(`[${run.label}] exit ${code} after ${seconds} s\n`);
  return interrupted ? 130 : outcomes.every(outcome => outcome.code === 0) ? 0 : 1;
}

function verify(manifest, resultsDirectory) {
  const partition = checkPartition(root, manifest);
  const errors = [...partition.errors];
  let resultsSummary = '';
  if (resultsDirectory) {
    const results = checkResults(path.resolve(root, resultsDirectory), manifest, partition);
    errors.push(...results.errors);
    resultsSummary = `; ${results.files ?? 0} TRX reports (${[...results.counts].map(([suite, count]) => `${suite} ${count}`).join(', ')})`;
  }
  if (errors.length > 0) {
    for (const error of errors) process.stderr.write(`FAIL: ${error}\n`);
    process.stderr.write(`Test shard verification failed with ${errors.length} issue(s).\n`);
    return 1;
  }
  const described = [...partition.suites].map(([suiteId, entry]) => `${suiteId} ${entry.classes.length} classes in ${entry.suite.shards.length} shards`);
  process.stdout.write(`PASS: every test class is in exactly one shard (${described.join('; ')})${resultsSummary}.\n`);
  return 0;
}

function inventory(manifest, suiteId) {
  const partition = checkPartition(root, manifest);
  const entry = partition.suites.get(suiteId);
  if (!entry) throw new Error(`Unknown suite: ${suiteId}`);
  const rows = entry.suite.partition === 'class' ? classifyClasses({ ...entry.scan, classes: entry.classes }) : entry.classes;
  process.stdout.write('class\tnamespace\tcases\tcluster\tdisrupts\tplace\n');
  for (const testClass of rows) {
    const places = [...new Set(testClass.methods.map(method => entry.ownerOf(testClass, method.name)).filter(Boolean)
      .map(owner => `${owner.shard}/${owner.lane ?? '-'}`))];
    process.stdout.write(`${testClass.name}\t${testClass.namespace}\t${testClass.cases}\t${testClass.cluster ?? '-'}\t${(testClass.disrupts ?? []).join(',') || '-'}\t${places.join(',') || 'none'}\n`);
  }
  for (const error of partition.errors) process.stderr.write(`FAIL: ${error}\n`);
  return partition.errors.length > 0 ? 1 : 0;
}

async function main(argumentsList) {
  const [command, ...rest] = argumentsList;
  const manifest = loadManifest(root);
  if (command === 'matrix' && rest.length === 1) {
    process.stdout.write(`${matrix(manifest, rest[0])}\n`);
    return 0;
  }
  if (command === 'run' && (rest.length === 1 || (rest.length === 3 && rest[1] === '--results-directory'))) {
    return runShard(manifest, rest[0], rest[2] ?? 'TestResults');
  }
  if (command === 'verify' && rest.length <= 1) return verify(manifest, rest[0]);
  if (command === 'verify-compiled' && rest.length === 2) {
    const partition = checkPartition(root, manifest);
    if (partition.errors.length > 0) throw new Error(partition.errors.join('\n'));
    const entry = partition.suites.get(rest[0]);
    if (!entry) throw new Error(`Unknown suite: ${rest[0]}`);
    const result = checkCompiledInventory(entry, JSON.parse(readFileSync(rest[1], 'utf8')));
    if (result.errors.length > 0) throw new Error(result.errors.join('\n'));
    process.stdout.write(`PASS: compiled ${rest[0]} discovery equals source inventory (${result.methods} methods, ${result.cases} cases).\n`);
    return 0;
  }
  if (command === 'inventory' && rest.length === 1) return inventory(manifest, rest[0]);
  process.stderr.write(`${usage}\n`);
  return 2;
}

process.stdout.on('error', error => {
  if (error.code !== 'EPIPE') throw error;
});

main(process.argv.slice(2)).then(code => { process.exitCode = code; }).catch(error => {
  process.stderr.write(`FAIL: ${error.message}\n`);
  process.exitCode = 1;
});
