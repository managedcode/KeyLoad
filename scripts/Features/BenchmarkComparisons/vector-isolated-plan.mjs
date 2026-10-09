import { isDeepStrictEqual } from 'node:util';
import { readIsolatedContract } from './isolated-plan.mjs';
import { isolatedPlanLimits, requireIsolatedPlan } from './isolated-plan-contract.mjs';

export const VECTOR_PROFILES = Object.freeze([
  ...[100_000, 1_000_000].flatMap(records => ['exact', 'hnsw', 'ivfflat', 'native'].flatMap(method =>
    ['plain', 'filtered', 'mixed'].map(workload => Object.freeze({
      id: `vector-${records === 100_000 ? '100k' : '1m'}-${method}-${workload}-c16`,
      records, method, workload,
    })))),
]);

const settings = Object.freeze({ dimensions: 128, metric: 'Cosine', topK: 10, seed: 1_729, payloadBytes: 1_024,
  queryVectorCount: 64, warmupQueries: 256, measuredQueries: 100_000, concurrency: 16,
  timeoutSeconds: 30, latencySampleCount: 4_096, repetitions: 1 });
const methods = new Set(['exact', 'hnsw', 'ivfflat', 'native']);
const workloads = new Set(['plain', 'filtered', 'mixed']);
const idPattern = /^vector-(?:100k|1m)-(?:exact|hnsw|ivfflat|native)-(?:plain|filtered|mixed)-c16$/u;

function profileSettings(profile) {
  const method = { exact: 'Exact', hnsw: 'Hnsw', ivfflat: 'IvfFlat', native: 'NativeAnn' }[profile.method];
  const queryMode = { plain: 'Plain', filtered: 'Filtered', mixed: 'Mixed' }[profile.workload];
  return { id: profile.id, recordCount: profile.records, indexKind: method, queryMode, ...settings,
    minimumRecall: profile.method === 'exact' ? 1 : 0.95, updateCount: profile.workload === 'mixed' ? 10_000 : 0 };
}

function createPlan(contract, profile) {
  requireIsolatedPlan(idPattern.test(profile.id) && methods.has(profile.method) && workloads.has(profile.workload));
  const cells = contract.targets.flatMap(target => contract.nodeCounts.map(nodeCount => ({
    id: `${target.toLowerCase().replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '')}-n${nodeCount}-vector-exact-${profile.id}`,
    target, nodeCount, scenario: 'VectorExact', profile: profile.id, family: 'vector',
  })));
  requireIsolatedPlan(cells.length === contract.targets.length * contract.nodeCounts.length && cells.every(cell => cell.id.length <= isolatedPlanLimits.idLength));
  return { schemaVersion: 1, workerSchemaVersion: contract.workerSchemaVersion, profile: profile.id,
    profileSettings: profileSettings(profile), cells, matrices: { vector: { include: cells } } };
}

export function createVectorPlans(contract = readIsolatedContract()) {
  return VECTOR_PROFILES.map(profile => createPlan(contract, profile));
}

export function validateVectorPlans(plans, contract = readIsolatedContract()) {
  const expected = createVectorPlans(contract);
  requireIsolatedPlan(isDeepStrictEqual(plans, expected));
  return expected.map(plan => structuredClone(plan));
}

export function validateVectorPlan(plan, contract = readIsolatedContract()) {
  const canonical = createVectorPlans(contract).find(item => item.profile === plan?.profile);
  requireIsolatedPlan(canonical !== undefined && isDeepStrictEqual(plan, canonical));
  return structuredClone(canonical);
}

export function vectorProfileSettings(profileId) {
  const profile = VECTOR_PROFILES.find(item => item.id === profileId);
  requireIsolatedPlan(profile !== undefined);
  return profileSettings(profile);
}

export function createVectorPlan(contract = readIsolatedContract()) {
  return { schemaVersion: 1, workerSchemaVersion: contract.workerSchemaVersion,
    profiles: createVectorPlans(contract) };
}
