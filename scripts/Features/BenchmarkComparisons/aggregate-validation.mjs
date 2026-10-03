import { AGGREGATE, KEYS, SUPPORT, exactKeys, positive, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { validateReport } from './aggregate-report.mjs';
export { validateAggregateProof } from './aggregate-proof.mjs';

// This validates structure and agreement, never the authenticity of a supplied GitHub proof.
export function validateWorkerEnvelope(value, cell, cohort, contract) {
  const error = AGGREGATE.errors.envelope;
  validateCohort(cohort, contract.profile);
  requireValue(exactKeys(cell, KEYS.cell) && contract.targets.includes(cell.target) && contract.nodeCounts.includes(cell.nodeCount) &&
    [...contract.crudScenarios, ...contract.specializedScenarios].includes(cell.scenario) &&
    cell.profile === contract.profile && Object.hasOwn(SUPPORT, cell.target), error);
  requireValue(exactKeys(value, KEYS.envelope) && value.schemaVersion === AGGREGATE.version &&
    exactKeys(value.worker, KEYS.worker), error);
  requireValue(['target', 'nodeCount', 'scenario', 'profile'].every(key => value.worker[key] === cell[key]) &&
    KEYS.cohort.every(key => value.worker[key] === cohort[key]) && positive(value.worker.jobId), error);
  const unsupported = contract.unsupportedTopologies.find(item => item.target === cell.target && item.nodeCounts.includes(cell.nodeCount));
  if (unsupported) {
    requireValue(value.disposition === AGGREGATE.unsupportedTopology && value.reason === unsupported.reason && value.report === null, error);
    return value;
  }
  requireValue(value.disposition === AGGREGATE.measured && value.reason === null, error);
  validateReport(value.report, cell, cohort, contract);
  return value;
}
