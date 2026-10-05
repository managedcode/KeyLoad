import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';

// Exact original git blob shared by these four immutable historical measured revisions.
export const HISTORICAL = Object.freeze({ sourceRevisions: Object.freeze([
  'aa49aa93982866b85a80e771a749d9b968ffc9f2', '73aebfd3f72695357834599e813aba77b9e274ad',
  '77167cbca9efe8942aab869dd52ad5b0b6cc72a1', 'ff0af70a279b6adce653bc5fe2527fef51f9de69']),
  path: 'benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/isolated-contract.json',
  gitBlob: '0a88b2f34c1ce2c27d69c1863ef3f236275327b1',
  sha256: '9edb7988a12976e1bc7a6885be3a5546a27bf0ba4430cb0ca80e65647c55152c',
  capture: 'source-contract.json', workerCount: 270, fileCount: 277 });
const check = condition => requireValue(condition, AGGREGATE.errors.proof);
const original = readFileSync(new URL('./historical-isolated-contract.json', import.meta.url));
const blobHash = bytes => createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
check(hash(original) === HISTORICAL.sha256 && blobHash(original) === HISTORICAL.gitBlob);
const contract = JSON.parse(original.toString('utf8'));

export const isHistoricalSource = revision => HISTORICAL.sourceRevisions.includes(revision);
export function readHistoricalContract(sourceRevision) {
  check(isHistoricalSource(sourceRevision));
  return structuredClone(contract);
}

export function createHistoricalPlan(sourceRevision) {
  const source = readHistoricalContract(sourceRevision);
  const slug = value => value.toLowerCase().replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '');
  const cells = (family, scenarios) => source.targets.flatMap(target => source.nodeCounts.flatMap(nodeCount =>
    scenarios.map(scenario => ({ id: `${slug(target)}-n${nodeCount}-${slug(scenario.replace(/([a-z0-9])([A-Z])/gu, '$1-$2'))}`,
      target, nodeCount, scenario, profile: source.profile, family }))));
  const crud = cells('crud', source.crudScenarios);
  const specialized = cells('specialized', source.specializedScenarios);
  check(crud.length === 108 && specialized.length === 162);
  return { schemaVersion: source.schemaVersion, workerSchemaVersion: source.workerSchemaVersion, profile: source.profile,
    options: structuredClone(source.options), cells: [...crud, ...specialized],
    matrices: { crud: { include: crud }, specialized: { include: specialized } } };
}

export function validateHistoricalPlan(value, sourceRevision) {
  const expected = createHistoricalPlan(sourceRevision);
  check(isDeepStrictEqual(value, expected));
  return expected;
}

// Only the trusted GitHub transport can authenticate this response; the parser verifies exact source/blob bytes.
export function verifyHistoricalContractCapture(value, sourceRevision) {
  check(isHistoricalSource(sourceRevision) && value?.type === 'file' && value.path === HISTORICAL.path &&
    value.name === 'isolated-contract.json' && value.sha === HISTORICAL.gitBlob && value.encoding === 'base64' &&
    value.size === original.length && value.html_url === `https://github.com/managedcode/KeyLoad/blob/${sourceRevision}/${HISTORICAL.path}` &&
    value.git_url === `https://api.github.com/repos/managedcode/KeyLoad/git/blobs/${HISTORICAL.gitBlob}` &&
    typeof value.content === 'string' && /^[A-Za-z0-9+/=\n\r]+$/u.test(value.content));
  const bytes = Buffer.from(value.content.replace(/[\r\n]/gu, ''), 'base64');
  check(bytes.equals(original) && hash(bytes) === HISTORICAL.sha256 && blobHash(bytes) === value.sha);
  return readHistoricalContract(sourceRevision);
}
