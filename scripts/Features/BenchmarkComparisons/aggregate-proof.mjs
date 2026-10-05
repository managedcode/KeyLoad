import { AGGREGATE, KEYS, exactKeys, matches, positive, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { readIsolatedContract, validateIsolatedPlan } from './isolated-plan.mjs';
import { isolatedEvidenceJobName, matchesIsolatedJobName } from './isolated-github-contract.mjs';
import { validateScaledPlan } from './scaled-isolated-plan.mjs';
import { validateVectorPlan } from './vector-isolated-plan.mjs';
import { validateScaleResourceProof } from './server-resource-evidence.mjs';
import { SITE_GH } from './site-isolated-github-contract.mjs';
import { validateHistoricalProof } from './historical-site-evidence.mjs';
import { validateHistoricalPlan } from './historical-isolated-plan.mjs';

const ERROR = AGGREGATE.errors.proof;
const ARTIFACT_PREFIX = 'comparison-worker-';
const GITHUB = 'https://github.com/';

function validateJob(job, cell, cohort, identities) {
  const nameMatches = cell.profile === readIsolatedContract().profile
    ? matchesIsolatedJobName(job?.name, cell) : job?.name === isolatedEvidenceJobName(cell);
  requireValue(exactKeys(job, KEYS.job) && positive(job.id) && !identities.has(job.id) && nameMatches &&
    job.url === `${GITHUB}${cohort.repository}/actions/runs/${cohort.runId}/job/${job.id}` && [AGGREGATE.success, AGGREGATE.failure].includes(job.conclusion) &&
    Array.isArray(job.steps) && job.steps.length === AGGREGATE.steps.length, ERROR);
  identities.add(job.id);
  const steps = new Set();
  for (const step of job.steps) {
    requireValue(exactKeys(step, KEYS.step) && AGGREGATE.steps.includes(step.name) && !steps.has(step.name) &&
      step.conclusion === (step.name === AGGREGATE.steps[0] ? job.conclusion : AGGREGATE.success), ERROR);
    steps.add(step.name);
  }
}

function validateArtifact(artifact, cell, identities) {
  requireValue(exactKeys(artifact, KEYS.artifact) && positive(artifact.id) && !identities.has(artifact.id) &&
    artifact.name === ARTIFACT_PREFIX + cell.id && positive(artifact.sizeInBytes) &&
    matches(AGGREGATE.zipDigest, artifact.digest) && artifact.expired === false, ERROR);
  identities.add(artifact.id);
}

export function validateAggregateProof(value, plan) {
  const contract = readIsolatedContract();
  const historical = SITE_GH.legacySources.includes(value?.cohort?.sourceRevision);
  if (historical) {
    const canonical = validateHistoricalPlan(plan, value.cohort.sourceRevision);
    return validateHistoricalProof(value, canonical, value.cohort);
  }
  const canonical = plan?.profile === contract.profile ? validateIsolatedPlan(plan, contract)
    : plan?.profile?.startsWith('vector-') ? validateVectorPlan(plan, contract) : validateScaledPlan(plan, contract);
  requireValue(canonical.profile === plan.profile, ERROR);
  requireValue(exactKeys(value, KEYS.proof) && value.schemaVersion === AGGREGATE.proofVersion &&
    Array.isArray(value.cells) && value.cells.length === plan.cells.length, ERROR);
  validateCohort(value.cohort, plan.profile);
  const needsResource = canonical.profile !== contract.profile;
  const expected = new Map(plan.cells.map(cell => [cell.id, cell]));
  const seen = new Set();
  const jobs = new Set();
  const artifacts = new Set();
  for (const item of value.cells) {
    const cell = expected.get(item.id);
    requireValue(exactKeys(item, needsResource ? KEYS.scaleProofCell : KEYS.proofCell)
      && cell !== undefined && !seen.has(item.id) && matches(AGGREGATE.digest, item.workerSha256), ERROR);
    seen.add(item.id);
    if (needsResource) {
      const unsupported = contract.unsupportedTopologies.some(entry => entry.target === cell.target
        && entry.nodeCounts.includes(cell.nodeCount));
      validateScaleResourceProof(item.serverResource, !unsupported);
    }
    validateJob(item.job, cell, value.cohort, jobs);
    validateArtifact(item.artifact, expected.get(item.id), artifacts);
  }
  return value;
}
