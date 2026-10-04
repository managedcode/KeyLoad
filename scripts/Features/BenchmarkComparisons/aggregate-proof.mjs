import { AGGREGATE, KEYS, exactKeys, matches, positive, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { validateIsolatedPlan } from './isolated-plan.mjs';
import { matchesIsolatedJobName } from './isolated-github-contract.mjs';

const ERROR = AGGREGATE.errors.proof;
const ARTIFACT_PREFIX = 'comparison-worker-';
const GITHUB = 'https://github.com/';

function validateJob(job, cell, cohort, identities) {
  requireValue(exactKeys(job, KEYS.job) && positive(job.id) && !identities.has(job.id) && matchesIsolatedJobName(job.name, cell) &&
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
  validateIsolatedPlan(plan);
  requireValue(exactKeys(value, KEYS.proof) && value.schemaVersion === AGGREGATE.proofVersion &&
    Array.isArray(value.cells) && value.cells.length === plan.cells.length, ERROR);
  validateCohort(value.cohort, plan.profile);
  const expected = new Map(plan.cells.map(cell => [cell.id, cell]));
  const seen = new Set();
  const jobs = new Set();
  const artifacts = new Set();
  for (const item of value.cells) {
    requireValue(exactKeys(item, KEYS.proofCell) && expected.has(item.id) && !seen.has(item.id) &&
      matches(AGGREGATE.digest, item.workerSha256), ERROR);
    seen.add(item.id);
    validateJob(item.job, expected.get(item.id), value.cohort, jobs);
    validateArtifact(item.artifact, expected.get(item.id), artifacts);
  }
  return value;
}
