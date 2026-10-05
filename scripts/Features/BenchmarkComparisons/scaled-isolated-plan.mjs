import { isDeepStrictEqual } from 'node:util';
import { createIsolatedPlan, readIsolatedContract, validateIsolatedPlan } from './isolated-plan.mjs';
import { isolatedPlanLimits, requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { createVectorPlans, validateVectorPlans } from './vector-isolated-plan.mjs';

export const SCALED_PROFILES = Object.freeze([
  Object.freeze({ id: 'scaled-100k-c16', documents: 100_000 }),
  Object.freeze({ id: 'scaled-1m-c16', documents: 1_000_000 }),
]);

const settings = Object.freeze({ operations: 100_000, warmup: 256, repetitions: 1, concurrency: 16,
  payloadBytes: 1_024, seed: 1_729, dimensions: 32, topK: 10, timeoutSeconds: 30,
  graphVertices: 0, graphFanOut: 0, graphDepth: 0 });
const idPattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/u;

function createCells(contract, profile) {
  const cells = contract.targets.flatMap(target => contract.nodeCounts.flatMap(nodeCount =>
    contract.crudScenarios.map(scenario => {
      const base = `${target.toLowerCase().replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '')}-n${nodeCount}-` +
        scenario.replace(/([a-z0-9])([A-Z])/gu, '$1-$2').toLowerCase();
      const id = `${base}-${profile.id}`;
      requireIsolatedPlan(id.length <= isolatedPlanLimits.idLength && idPattern.test(id));
      return { id, target, nodeCount, scenario, profile: profile.id, family: 'crud' };
    })));
  requireIsolatedPlan(cells.length === 132 && new Set(cells.map(cell => cell.id)).size === cells.length);
  return cells;
}

function createProfilePlan(contract, profile) {
  const cells = createCells(contract, profile);
  const crud = cells;
  const specialized = [];
  return { schemaVersion: 1, workerSchemaVersion: contract.workerSchemaVersion,
    profile: profile.id, profileSettings: scaleProfileSettings(profile.id), cells,
    matrices: { crud: { include: crud }, specialized: { include: specialized } } };
}

export function createScaledPlans(contract = readIsolatedContract()) {
  return SCALED_PROFILES.map(profile => createProfilePlan(contract, profile));
}

export function createCompositePlan(controlPlan = createIsolatedPlan(), scaledPlans = createScaledPlans(), vectorPlans = createVectorPlans()) {
  const control = validateIsolatedPlan(controlPlan);
  const scales = validateScaledPlans(scaledPlans);
  const vectors = validateVectorPlans(vectorPlans);
  return { schemaVersion: 3, control, scaledProfiles: scales, vectorProfiles: vectors };
}

export function validateCompositePlan(value, contract = readIsolatedContract()) {
  const expected = createCompositePlan(createIsolatedPlan(contract), createScaledPlans(contract), createVectorPlans(contract));
  requireIsolatedPlan(isDeepStrictEqual(value, expected));
  return structuredClone(expected);
}

export function validateScaledPlans(plans, contract = readIsolatedContract()) {
  const expected = createScaledPlans(contract);
  requireIsolatedPlan(isDeepStrictEqual(plans, expected));
  return expected.map(plan => structuredClone(plan));
}

export function validateScaledPlan(plan, contract = readIsolatedContract()) {
  const canonical = createScaledPlans(contract).find(item => item.profile === plan?.profile);
  requireIsolatedPlan(canonical !== undefined && isDeepStrictEqual(plan, canonical));
  return structuredClone(canonical);
}

export function scaleProfileSettings(profileId) {
  const profile = SCALED_PROFILES.find(item => item.id === profileId);
  requireIsolatedPlan(profile !== undefined);
  return { ...profile, ...settings };
}

export const scaledCellCount = SCALED_PROFILES.length * 132;
