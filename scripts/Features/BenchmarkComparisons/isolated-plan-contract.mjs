import { readFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';

const contractUrl = new URL('../../../benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/isolated-contract.json', import.meta.url);
const contractFields = ['schemaVersion', 'workerSchemaVersion', 'profile', 'targets', 'nodeCounts',
  'crudScenarios', 'specializedScenarios', 'options', 'unsupportedTopologies'];
const optionFields = ['seed', 'documents', 'operations', 'warmup', 'repetitions', 'concurrency',
  'payloadBytes', 'dimensions', 'topK', 'graphVertices', 'graphFanOut', 'graphDepth', 'timeoutSeconds'];
const canonical = JSON.parse(readFileSync(contractUrl, 'utf8'));

export const isolatedPlanLimits = Object.freeze({
  cells: 330, crud: 132, specialized: 198, matrix: 256, idLength: 120,
});

export function requireIsolatedPlan(condition) {
  if (!condition) throw new Error('The isolated comparison plan is invalid.');
}

function closedObject(value, fields) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    && isDeepStrictEqual(Object.keys(value).sort(), [...fields].sort());
}

function uniqueArray(value, count, predicate) {
  return Array.isArray(value) && value.length === count && new Set(value).size === count
    && value.every(predicate);
}

function hasText(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= isolatedPlanLimits.idLength
    && !/[\u0000-\u001f\u007f]/u.test(value);
}

function validateOptions(options) {
  requireIsolatedPlan(closedObject(options, optionFields));
  requireIsolatedPlan(Object.values(options).every(value => Number.isSafeInteger(value) && value > 0));
  requireIsolatedPlan(options.warmup < options.operations && options.topK <= options.documents
    && options.graphFanOut < options.graphVertices);
}

function validateUnsupported(entries, contract) {
  requireIsolatedPlan(Array.isArray(entries));
  const ids = [];
  for (const entry of entries) {
    requireIsolatedPlan(closedObject(entry, ['target', 'nodeCounts', 'reason']));
    requireIsolatedPlan(contract.targets.includes(entry.target) && typeof entry.reason === 'string'
      && entry.reason.trim().length > 0);
    requireIsolatedPlan(Array.isArray(entry.nodeCounts) && entry.nodeCounts.length > 0
      && new Set(entry.nodeCounts).size === entry.nodeCounts.length
      && entry.nodeCounts.every(value => contract.nodeCounts.includes(value) && value > 1));
    ids.push(...entry.nodeCounts.map(value => entry.target + '/' + value));
  }
  requireIsolatedPlan(new Set(ids).size === ids.length);
}

function validateCanonicalSource(contract) {
  requireIsolatedPlan(closedObject(contract, contractFields));
  requireIsolatedPlan(contract.schemaVersion === 1 && contract.workerSchemaVersion === 5);
  requireIsolatedPlan(hasText(contract.profile) && /^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(contract.profile));
  requireIsolatedPlan(uniqueArray(contract.targets, 11, hasText));
  requireIsolatedPlan(isDeepStrictEqual(contract.nodeCounts, [1, 2, 3]));
  requireIsolatedPlan(uniqueArray(contract.crudScenarios, 4, hasText));
  requireIsolatedPlan(uniqueArray(contract.specializedScenarios, 6, hasText));
  const scenarios = [...contract.crudScenarios, ...contract.specializedScenarios];
  requireIsolatedPlan(new Set(scenarios).size === scenarios.length);
  validateOptions(contract.options);
  validateUnsupported(contract.unsupportedTopologies, contract);
}

validateCanonicalSource(canonical);

export function readIsolatedContract() {
  return structuredClone(canonical);
}

export function requireCanonicalContract(contract) {
  requireIsolatedPlan(isDeepStrictEqual(contract, canonical));
}
