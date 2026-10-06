import { isDeepStrictEqual } from 'node:util';
import { readIsolatedContract, requireCanonicalContract, isolatedPlanLimits, requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { createScaledPlans, SCALED_PROFILES } from './scaled-isolated-plan.mjs';

const PLAN_SCHEMA_VERSION = 1;
const PLAN_KIND = 'open-loop-isolated-plan.v1';
const OPEN_LOOP_FAMILY = 'open-loop';
const PROOF_FAMILY = 'open-loop-proof';
const OPEN_LOOP_SUFFIX = '-openloop-r';
const PROOF_SUFFIX = '-openloop-proof-r';
const OFFERED_RATES = Object.freeze([250, 1_000, 4_000]);
const EXPECTED_PROFILE_COUNT = 2;
const EXPECTED_MEASUREMENT_COUNT = 792;
const EXPECTED_PROOF_COUNT = 6;
const EXPECTED_UNSUPPORTED_COUNT = 144;
const EXPECTED_MEASUREMENTS_PER_TARGET = 72;
const EXPECTED_TARGET_COUNT = 11;
const EXPECTED_NODE_COUNT = 3;
const EXPECTED_SCENARIO_COUNT = 4;
const PLAN_FIELDS = Object.freeze(['schemaVersion', 'kind', 'measurementCells', 'cancellationProofCells']);
const CELL_FIELDS = Object.freeze(['id', 'target', 'nodeCount', 'scenario', 'profile', 'family', 'offeredRatePerSecond', 'cancellationProof']);
const ID_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/u;

function identityKey(target, nodeCount, scenario, profile) {
  return JSON.stringify([target, nodeCount, scenario, profile]);
}

function indexScaledCells(plans) {
  requireIsolatedPlan(plans.length === EXPECTED_PROFILE_COUNT && plans.length === SCALED_PROFILES.length);
  const indexed = new Map();
  for (const plan of plans) {
    for (const cell of plan.cells) {
      const key = identityKey(cell.target, cell.nodeCount, cell.scenario, cell.profile);
      requireIsolatedPlan(!indexed.has(key));
      indexed.set(key, cell);
    }
  }
  return indexed;
}

function unsupportedIdentities(contract) {
  const identities = new Set();
  for (const topology of contract.unsupportedTopologies) {
    for (const nodeCount of topology.nodeCounts) {
      identities.add(identityKey(topology.target, nodeCount, '*', '*'));
    }
  }
  return identities;
}

function isUnsupported(identities, target, nodeCount) {
  return identities.has(identityKey(target, nodeCount, '*', '*'));
}

function scaledCell(indexed, target, nodeCount, scenario, profile) {
  const cell = indexed.get(identityKey(target, nodeCount, scenario, profile));
  requireIsolatedPlan(cell !== undefined);
  return cell;
}

function createCell(base, family, rate, cancellationProof, suffix) {
  const id = base.id + suffix + rate;
  requireIsolatedPlan(id.length <= isolatedPlanLimits.idLength && ID_PATTERN.test(id));
  return {
    id,
    target: base.target,
    nodeCount: base.nodeCount,
    scenario: base.scenario,
    profile: base.profile,
    family,
    offeredRatePerSecond: rate,
    cancellationProof,
  };
}

function createMeasurements(contract, indexed) {
  requireIsolatedPlan(contract.targets.length === EXPECTED_TARGET_COUNT && contract.nodeCounts.length === EXPECTED_NODE_COUNT
    && contract.crudScenarios.length === EXPECTED_SCENARIO_COUNT && SCALED_PROFILES.length === EXPECTED_PROFILE_COUNT);
  const cells = new Array(EXPECTED_MEASUREMENT_COUNT);
  for (let index = 0; index < EXPECTED_MEASUREMENT_COUNT; index++) {
    const rate = OFFERED_RATES[index % OFFERED_RATES.length];
    const profile = SCALED_PROFILES[Math.floor(index / OFFERED_RATES.length) % EXPECTED_PROFILE_COUNT];
    const scenario = contract.crudScenarios[Math.floor(index / (OFFERED_RATES.length * EXPECTED_PROFILE_COUNT)) % contract.crudScenarios.length];
    const nodeCount = contract.nodeCounts[Math.floor(index / (OFFERED_RATES.length * EXPECTED_PROFILE_COUNT * contract.crudScenarios.length)) % contract.nodeCounts.length];
    const target = contract.targets[Math.floor(index / (OFFERED_RATES.length * EXPECTED_PROFILE_COUNT * contract.crudScenarios.length * contract.nodeCounts.length))];
    const base = scaledCell(indexed, target, nodeCount, scenario, profile.id);
    cells[index] = createCell(base, OPEN_LOOP_FAMILY, rate, false, OPEN_LOOP_SUFFIX);
  }
  return cells;
}

function createProofs(contract, indexed, unsupported) {
  const target = contract.targets.find(value => value === 'KeyLoad');
  const nodeCount = contract.nodeCounts.find(value => value === 3);
  const scenario = contract.crudScenarios.find(value => value === 'PointRead');
  requireIsolatedPlan(target !== undefined && nodeCount !== undefined && scenario !== undefined);
  requireIsolatedPlan(!isUnsupported(unsupported, target, nodeCount));
  const cells = [];
  for (const profile of SCALED_PROFILES) {
    const base = scaledCell(indexed, target, nodeCount, scenario, profile.id);
    for (const rate of OFFERED_RATES) {
      cells.push(createCell(base, PROOF_FAMILY, rate, true, PROOF_SUFFIX));
    }
  }
  requireIsolatedPlan(cells.length === EXPECTED_PROOF_COUNT);
  return cells;
}

function validateInventory(measurements, proofs, contract, unsupported) {
  const measurementIds = new Set(measurements.map(cell => cell.id));
  requireIsolatedPlan(measurementIds.size === EXPECTED_MEASUREMENT_COUNT);
  const unsupportedCount = measurements.filter(cell => isUnsupported(unsupported, cell.target, cell.nodeCount)).length;
  requireIsolatedPlan(unsupportedCount === EXPECTED_UNSUPPORTED_COUNT);
  for (const target of contract.targets) {
    requireIsolatedPlan(measurements.filter(cell => cell.target === target).length === EXPECTED_MEASUREMENTS_PER_TARGET);
  }
  const proofIds = new Set(proofs.map(cell => cell.id));
  requireIsolatedPlan(proofIds.size === EXPECTED_PROOF_COUNT);
  requireIsolatedPlan(proofs.every(cell => !measurementIds.has(cell.id)
    && !isUnsupported(unsupported, cell.target, cell.nodeCount)));
}

export function createOpenLoopPlan(contract = readIsolatedContract()) {
  requireCanonicalContract(contract);
  const indexed = indexScaledCells(createScaledPlans(contract));
  const unsupported = unsupportedIdentities(contract);
  const measurementCells = createMeasurements(contract, indexed);
  const cancellationProofCells = createProofs(contract, indexed, unsupported);
  validateInventory(measurementCells, cancellationProofCells, contract, unsupported);
  return structuredClone({
    schemaVersion: PLAN_SCHEMA_VERSION,
    kind: PLAN_KIND,
    measurementCells,
    cancellationProofCells,
  });
}

export function validateOpenLoopPlan(value, contract = readIsolatedContract()) {
  const expected = createOpenLoopPlan(contract);
  requireIsolatedPlan(hasCanonicalFieldOrder(value) && isDeepStrictEqual(value, expected));
  return structuredClone(expected);
}

function hasCanonicalFieldOrder(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)
    || !sameKeys(Object.keys(value), PLAN_FIELDS)
    || !Array.isArray(value.measurementCells) || value.measurementCells.length !== EXPECTED_MEASUREMENT_COUNT
    || !Array.isArray(value.cancellationProofCells) || value.cancellationProofCells.length !== EXPECTED_PROOF_COUNT) {
    return false;
  }
  return hasCellOrder(value.measurementCells) && hasCellOrder(value.cancellationProofCells);
}

function hasCellOrder(cells) {
  return Array.isArray(cells) && cells.every(cell => cell !== null && typeof cell === 'object'
    && !Array.isArray(cell) && sameKeys(Object.keys(cell), CELL_FIELDS));
}

function sameKeys(actual, expected) {
  return actual.length === expected.length && actual.every((key, index) => key === expected[index]);
}
