import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { checkCompiledInventory } from './ci-test-compiled-inventory.mjs';
import { checkPartition, loadManifest } from './ci-test-partition.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const partition = checkPartition(root, loadManifest(root));
assert.deepEqual(partition.errors, []);
function discover(entry) {
  const binaryDirectory = path.join(root, path.dirname(entry.suite.project), 'bin/Debug/net10.0');
  const evidence = path.join(binaryDirectory, `compiled-shard-inventory-${randomUUID()}`);
  const ipc = path.join(root, 'tests/bin/Debug/ipc');
  const categories = (entry.suite.excludedCategories ?? []).map(category => `[Category!=${category}]`).join('');
  mkdirSync(evidence, { recursive: true });
  mkdirSync(ipc, { recursive: true });
  const original = spawnSync('dotnet', [path.join(binaryDirectory, `${entry.suite.assembly}.dll`),
    '--list-tests', 'json', '--disable-logo', '--ansi', 'off', '--progress', 'off', '--output', 'Detailed',
    '--treenode-filter', `/${entry.suite.assembly}/*/*/*${categories}`, '--results-directory', evidence], {
    cwd: root, encoding: 'utf8', timeout: 120000, maxBuffer: 32 * 1024 * 1024,
    env: { ...process.env, TMPDIR: ipc, TMP: ipc, TEMP: ipc, TESTINGPLATFORM_PIPE_DIRECTORY: ipc },
  });
  writeFileSync(path.join(evidence, 'execution-list.json'), original.stdout ?? '');
  writeFileSync(path.join(evidence, 'execution-list.stderr.txt'), original.stderr ?? '');
  assert.equal(original.status, 0, `Compiled discovery failed; original evidence: ${evidence}`);
  return { census: JSON.parse(original.stdout), evidence };
}

const observed = new Map([...partition.suites].map(([id, owner]) => [id, discover(owner)]));
const entry = partition.suites.get('cluster');
const { census } = observed.get('cluster');
const qualifiedClasses = ['SqlBetweenSymmetricClusterWholeFlowTests', 'SqlBooleanTruthClusterWholeFlowTests',
  'SqlFetchCountClusterWholeFlowTests', 'SqlNullDistinctClusterWholeFlowTests', 'SqlNullOrderingClusterWholeFlowTests'];

test('source inventory includes every existing SQL class with qualified TestAttribute', () => {
  for (const name of qualifiedClasses) {
    const actual = entry.classes.find(type => type.name === name);
    assert.ok(actual, `${name} must be discovered`);
    assert.equal(actual.methods.length, 1);
  }
});

test('actual compiled TUnit discovery equals every source method selected by CI', () => {
  for (const [suite, owner] of partition.suites) {
    const { census: actual, evidence } = observed.get(suite);
    const result = checkCompiledInventory(owner, actual);
    assert.deepEqual(result.errors, []);
    console.log(`Original compiled ${suite} discovery: ${result.methods} methods, ${result.cases} cases; evidence: ${evidence}`);
  }
});

test('compiled verification rejects the original five omitted SQL classes', () => {
  const omitted = { ...entry, classes: entry.classes.filter(type => !qualifiedClasses.includes(type.name)) };
  const result = checkCompiledInventory(omitted, census);
  assert.equal(result.errors.length, qualifiedClasses.length);
  for (const name of qualifiedClasses) assert.ok(result.errors.some(error => error.includes(`.${name}.`)));
});

test('compiled verification rejects a source method that TUnit did not discover', () => {
  const missing = census.tests[0].type;
  const incomplete = { ...census, tests: census.tests.filter(item =>
    item.type.typeName !== missing.typeName || item.type.methodName !== missing.methodName) };
  const result = checkCompiledInventory(entry, incomplete);
  assert.ok(result.errors.some(error => error.startsWith('Source test method is absent from compiled discovery:')));
});
