import { ISOLATED, WIRE, assertIsolated, date, exact, matches, matchesIsolatedJobName, positive, rawPath, runUrl, same } from './isolated-contracts.mjs';

export function validateCohort(value) {
  assertIsolated(exact(value, WIRE.cohort) && matches(ISOLATED.sha, value.sourceRevision) && positive(value.runId) &&
    positive(value.attempt) && value.repository === ISOLATED.repository && value.ref === ISOLATED.ref &&
    value.workflow === ISOLATED.workflow && value.profile === ISOLATED.profile);
  return value;
}

export function validateOptions(value, nodeCount) {
  const keys = Object.keys(ISOLATED.options);
  const expected = nodeCount === undefined ? keys : [...keys, 'topology'];
  assertIsolated(exact(value, expected) && same(value, ISOLATED.options, keys) &&
    (nodeCount === undefined || value.topology === ISOLATED.topology[nodeCount]));
}

function validateJob(job, cell, cohort, jobs, disposition) {
  const failed = disposition === ISOLATED.failed;
  assertIsolated(exact(job, WIRE.job) && positive(job.id) && !jobs.has(job.id) && matchesIsolatedJobName(job.name, cell) &&
    job.url === `${runUrl(cohort)}/job/${job.id}` && job.conclusion === (failed ? ISOLATED.failure : ISOLATED.success) &&
    Array.isArray(job.steps) && job.steps.length === ISOLATED.steps.length);
  jobs.add(job.id);
  const seen = new Set();
  for (const step of job.steps) {
    assertIsolated(exact(step, WIRE.step) && ISOLATED.steps.includes(step.name) && !seen.has(step.name) &&
      step.conclusion === (failed && step.name === ISOLATED.steps[0] ? ISOLATED.failure : ISOLATED.success));
    seen.add(step.name);
  }
}

function validateArtifact(artifact, id, artifacts) {
  assertIsolated(exact(artifact, WIRE.artifact) && positive(artifact.id) && !artifacts.has(artifact.id) &&
    artifact.name === 'comparison-worker-' + id && positive(artifact.sizeInBytes) &&
    matches(/^sha256:[a-f0-9]{64}$/, artifact.digest) && artifact.expired === false);
  artifacts.add(artifact.id);
}

export function validateWorkerMetadata(worker, cell, cohort, identities, keys = WIRE.worker) {
  assertIsolated(exact(worker, keys) && same(worker, cell, ['id', 'target', 'nodeCount', 'scenario', 'profile']) &&
    worker.rawPath === rawPath(cell.id) && matches(ISOLATED.hash, worker.rawSha256));
  validateJob(worker.job, cell, cohort, identities.jobs, worker.disposition);
  validateArtifact(worker.artifact, cell.id, identities.artifacts);
  const unsupported = ISOLATED.unsupportedTopologies.find(item => item.target === cell.target &&
    item.nodeCounts.includes(cell.nodeCount));
  assertIsolated(worker.disposition === ISOLATED.failed ? worker.reason === ISOLATED.failureReason : unsupported
    ? worker.disposition === 'unsupportedTopology' && worker.reason === unsupported.reason
    : worker.disposition === 'measured' && worker.reason === null);
}

export function validateCatalog(value) {
  assertIsolated(exact(value, WIRE.catalog) && value.schemaVersion === ISOLATED.catalogVersion && date(value.generatedAt) &&
    matches(ISOLATED.sha, value.siteSourceRevision) && matches(ISOLATED.sha, value.measuredSourceRevision) &&
    value.rawLocation === ISOLATED.rawLocation);
  validateCohort(value.cohort);
  assertIsolated(value.measuredSourceRevision === value.cohort.sourceRevision && value.evidenceUrl === runUrl(value.cohort));
  for (const [key, path] of [['aggregate', ISOLATED.aggregatePath], ['projection', ISOLATED.projectionPath]]) {
    assertIsolated(exact(value[key], WIRE.file) && value[key].path === path && matches(ISOLATED.hash, value[key].sha256));
  }
  return value;
}
