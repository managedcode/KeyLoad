import { createOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { requireIsolatedPlan } from './isolated-plan-contract.mjs';

const SELECTOR = Object.freeze({
  rate: 'KEYLOAD_OPEN_LOOP_RATE',
  proof: 'KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF',
  cell: 'KEYLOAD_COMPARISON_CELL_ID',
  target: 'Benchmarks__Target',
  nodeCount: 'Benchmarks__NodeCount',
  scenario: 'Benchmarks__Scenario',
  profile: 'Benchmarks__EvidenceProfile',
  nativeRate: 'Benchmarks__OpenLoopRate',
  nativeProof: 'Benchmarks__OpenLoopCancellationProof',
  identityMarker: '-openloop-',
  measuredFilter: '/*/*/IsolatedNativeOpenLoopComparisonTests/*',
  proofFilter: '/*/*/IsolatedNativeOpenLoopCancellationTests/*',
  rateArgument: '--KeyLoadTests:OpenLoopRate=',
});

function absent(value) {
  return value === undefined || value === '';
}

export function selectOpenLoopWorkload(environment, scaleProfile, vectorProfile) {
  requireIsolatedPlan(environment[SELECTOR.nativeRate] === undefined
    && environment[SELECTOR.nativeProof] === undefined);
  const rate = environment[SELECTOR.rate];
  const proof = environment[SELECTOR.proof];
  const identity = environment[SELECTOR.cell];
  if (absent(rate) && absent(proof)) {
    requireIsolatedPlan(typeof identity === 'string' && !identity.includes(SELECTOR.identityMarker));
    return undefined;
  }
  requireIsolatedPlan(typeof rate === 'string' && typeof proof === 'string'
    && scaleProfile !== undefined && vectorProfile === undefined);
  const plan = createOpenLoopPlan();
  const cell = [...plan.measurementCells, ...plan.cancellationProofCells]
    .find(candidate => candidate.id === identity);
  requireIsolatedPlan(cell !== undefined && String(cell.offeredRatePerSecond) === rate
    && String(cell.cancellationProof) === proof
    && cell.target === environment[SELECTOR.target]
    && String(cell.nodeCount) === environment[SELECTOR.nodeCount]
    && cell.scenario === environment[SELECTOR.scenario]
    && cell.profile === environment[SELECTOR.profile]
    && cell.profile === scaleProfile);
  return cell;
}

export function openLoopWorkloadArguments(cell) {
  const plan = createOpenLoopPlan();
  const canonical = [...plan.measurementCells, ...plan.cancellationProofCells]
    .find(candidate => candidate.id === cell?.id);
  requireIsolatedPlan(canonical !== undefined
    && Object.keys(cell).length === Object.keys(canonical).length
    && Object.entries(canonical).every(([key, value]) => cell[key] === value));
  return {
    filter: canonical.cancellationProof ? SELECTOR.proofFilter : SELECTOR.measuredFilter,
    rateArgument: SELECTOR.rateArgument + canonical.offeredRatePerSecond,
  };
}
