import { AGGREGATE, KEYS, SUPPORT, VECTOR_SUPPORT, exactKeys, positive, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { validateReport } from './aggregate-report.mjs';
import { vectorProfileSettings } from './vector-isolated-plan.mjs';
export { validateAggregateProof } from './aggregate-proof.mjs';

// This validates structure and agreement, never the authenticity of a supplied GitHub proof.
export function validateWorkerEnvelope(value, cell, cohort, contract) {
  const error = AGGREGATE.errors.envelope;
  validateCohort(cohort, contract.profile);
  requireValue(exactKeys(cell, KEYS.cell) && contract.targets.includes(cell.target) && contract.nodeCounts.includes(cell.nodeCount) &&
    (cell.profile.startsWith('vector-') ? cell.scenario === 'VectorExact'
      : [...contract.crudScenarios, ...contract.specializedScenarios].includes(cell.scenario)) &&
    cell.profile === contract.profile && Object.hasOwn(SUPPORT, cell.target), error);
  requireValue(exactKeys(value, KEYS.envelope) && value.schemaVersion === AGGREGATE.version &&
    exactKeys(value.worker, KEYS.worker), error);
  requireValue(['target', 'nodeCount', 'scenario', 'profile'].every(key => value.worker[key] === cell[key]) &&
    KEYS.cohort.every(key => value.worker[key] === cohort[key]) && positive(value.worker.jobId), error);
  if (value.disposition === AGGREGATE.failed) {
    requireValue(value.reason === AGGREGATE.failureReason && value.report === null, error);
    return value;
  }
  if (cell.profile.startsWith('vector-')) {
    const profile = vectorProfileSettings(cell.profile);
    const topology = contract.unsupportedTopologies.find(item => item.target === cell.target && item.nodeCounts.includes(cell.nodeCount));
    if (topology) {
      requireValue(value.disposition === AGGREGATE.unsupportedTopology && value.reason === topology.reason && value.report === null, error);
      return value;
    }
    if (cell.target === 'KeyLoad') {
      requireValue(value.disposition === AGGREGATE.unsupported && value.reason ===
        'KeyLoad SDK does not expose persisted vector readback or native numeric predicates required for scaled vector qualification.' && value.report === null, error);
      return value;
    }
    if (!VECTOR_SUPPORT[cell.target].includes(profile.indexKind)) {
      requireValue(value.disposition === AGGREGATE.unsupported && value.reason ===
        `${cell.target} does not implement ${profile.indexKind}/${profile.queryMode} natively.` && value.report === null, error);
      return value;
    }
    requireValue(value.disposition === AGGREGATE.measured && value.reason === null, error);
    validateReport(value.report, cell, cohort, { ...contract, profile: cell.profile });
    return value;
  }
  const unsupported = contract.unsupportedTopologies.find(item => item.target === cell.target && item.nodeCounts.includes(cell.nodeCount));
  if (unsupported) {
    requireValue(value.disposition === AGGREGATE.unsupportedTopology && value.reason === unsupported.reason && value.report === null, error);
    return value;
  }
  requireValue(value.disposition === AGGREGATE.measured && value.reason === null, error);
  validateReport(value.report, cell, cohort, contract);
  return value;
}

export function requireWorkerJobAgreement(envelope, job) {
  requireValue((envelope.disposition === AGGREGATE.failed) === (job.conclusion === AGGREGATE.failure),
    AGGREGATE.errors.proof);
}
