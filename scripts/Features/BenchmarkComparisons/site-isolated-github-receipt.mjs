import { isDeepStrictEqual } from 'node:util';
import { KEYS, validateCohort } from './aggregate-contracts.mjs';
import { createIsolatedPlan } from './isolated-plan.mjs';
import { GH, digestPattern, hashPattern, matchesIsolatedJobName, isolatedEvidenceJobName, positive, shaPattern, timestamp } from './isolated-github-contract.mjs';
import { SITE_GH, artifactLimit, exact, requireSite, safeRelative, siteAggregateSteps,
  siteEvidencePlans, siteEvidenceSuiteFiles, siteEvidenceProviderFiles, siteEvidenceWorkerCount } from './site-isolated-github-contract.mjs';

function validateArtifact(value, name, limit) {
  requireSite(exact(value, SITE_GH.artifactKeys) && value.name === name && positive(value.id)
    && positive(value.sizeInBytes) && value.sizeInBytes <= limit && digestPattern.test(value.digest ?? '') && value.expired === false);
  timestamp(value.createdAt);
}

function validateAggregateJob(job, run, artifacts) {
  const steps = siteAggregateSteps();
  requireSite(exact(job, ['id', 'name', 'url', 'startedAt', 'completedAt', 'steps']) && positive(job.id)
    && job.name === SITE_GH.aggregateJob && job.url === `https://github.com/${SITE_GH.repository}/actions/runs/${run.id}/job/${job.id}`
    && Array.isArray(job.steps) && job.steps.length === steps.length);
  const start = timestamp(job.startedAt);
  const end = timestamp(job.completedAt);
  const names = new Set();
  const numbers = new Set();
  for (const [index, step] of job.steps.entries()) {
    requireSite(exact(step, ['name', 'number', 'conclusion']) && step.name === steps[index]
      && !names.has(step.name) && positive(step.number) && !numbers.has(step.number) && step.conclusion === 'success');
    names.add(step.name); numbers.add(step.number);
  }
  requireSite(start <= end && Object.values(artifacts).every(item => timestamp(item.createdAt) >= start && timestamp(item.createdAt) <= end));
}

function validateFiles(files, expectedCount, prefix) {
  requireSite(Array.isArray(files) && files.length > 0 && (expectedCount === null || files.length === expectedCount));
  const paths = new Set();
  for (const file of files) {
    requireSite(exact(file, SITE_GH.fileKeys) && safeRelative(file.path) && file.path.startsWith(prefix)
      && !paths.has(file.path.toLowerCase()) && positive(file.bytes) && hashPattern.test(file.sha256 ?? ''));
    paths.add(file.path.toLowerCase());
  }
  return paths;
}

function validateArchiveFields(receipt) {
  requireSite(exact(receipt.archives, ['suite', 'provider']));
  for (const name of ['suite', 'provider']) {
    const archive = receipt.archives[name];
    const artifact = receipt.artifacts[name];
    requireSite(exact(archive, SITE_GH.fileKeys) && archive.path === `archives/${artifact.name}.zip`
      && archive.bytes === artifact.sizeInBytes && `sha256:${archive.sha256}` === artifact.digest);
  }
  const expected = [...siteEvidenceSuiteFiles().map(file => 'input/' + file),
    ...siteEvidenceProviderFiles().map(file => 'input/' + file)];
  const files = validateFiles(receipt.inputFiles, expected.length, 'input/');
  requireSite(expected.every(file => files.has(file.toLowerCase())));
  requireSite(receipt.inputFiles.every(file => file.bytes <= (file.path.endsWith('/worker.json') ? SITE_GH.rawBytes : SITE_GH.jsonBytes)));
  requireSite(receipt.inputFiles.filter(file => file.path.endsWith('/worker.json')).length === siteEvidenceWorkerCount()
    && receipt.inputFiles.filter(file => file.path.endsWith('/worker.json')).reduce((sum, file) => sum + file.bytes, 0) <= SITE_GH.totalRawBytes);
}

function validateProjectedJob(job, cohort, name, requiredSteps, worker = false) {
  const failed = worker && job?.conclusion === 'failure';
  requireSite(exact(job, KEYS.job) && job.name === name && positive(job.id) && (job.conclusion === 'success' || failed)
    && job.url === `https://github.com/${SITE_GH.repository}/actions/runs/${cohort.runId}/job/${job.id}`
    && Array.isArray(job.steps) && job.steps.length === requiredSteps.length
    && job.steps.every((step, index) => exact(step, KEYS.step) && step.name === requiredSteps[index] && step.conclusion === (failed && index === 0 ? 'failure' : 'success')));
}

function validateWorkers(receipt, plan) {
  const cells = new Map(siteEvidencePlans().flatMap(item => item.cells).map(cell => [cell.id, cell]));
  const expected = new Set(cells.keys());
  requireSite(Array.isArray(receipt.workers) && receipt.workers.length === expected.size);
  for (const worker of receipt.workers) {
    requireSite(exact(worker, ['id', 'profile', 'job', 'artifact']) && expected.delete(worker.id)
      && worker.profile === cells.get(worker.id).profile);
    requireSite(worker.profile === plan.profile ? matchesIsolatedJobName(worker.job?.name, cells.get(worker.id))
      : worker.job?.name === isolatedEvidenceJobName(cells.get(worker.id)));
    validateProjectedJob(worker.job, receipt.cohort, worker.job.name, GH.workerSteps, true);
    validateArtifact(worker.artifact, GH.artifactPrefix + worker.id, GH.workerZipBytes);
  }
  requireSite(exact(receipt.image, ['job', 'artifact']));
  validateProjectedJob(receipt.image.job, receipt.cohort, GH.imageJob, GH.imageSteps);
  validateArtifact(receipt.image.artifact, GH.imageArtifact, GH.imageZipBytes);
  const jobs = [receipt.aggregateJob.id, receipt.image.job.id, ...receipt.workers.map(item => item.job.id)];
  const artifacts = [receipt.artifacts.suite.id, receipt.artifacts.provider.id, receipt.image.artifact.id, ...receipt.workers.map(item => item.artifact.id)];
  requireSite(new Set(jobs).size === jobs.length && new Set(artifacts).size === artifacts.length);
}

export function validateSiteIsolatedReceipt(receipt) {
  const plan = createIsolatedPlan();
  requireSite(exact(receipt, SITE_GH.receiptKeys) && receipt.schemaVersion === SITE_GH.version
    && [SITE_GH.publish, SITE_GH.validate].includes(receipt.mode) && receipt.publishEligible === (receipt.mode === SITE_GH.publish)
    && [SITE_GH.metadataState, SITE_GH.archiveState].includes(receipt.state));
  requireSite(exact(receipt.source, ['website', 'measured', 'control']) && Object.values(receipt.source).every(value => shaPattern.test(value ?? '')));
  requireSite(exact(receipt.repository, ['id', 'fullName']) && receipt.repository.id === SITE_GH.repositoryId && receipt.repository.fullName === SITE_GH.repository
    && exact(receipt.workflow, ['id', 'path']) && positive(receipt.workflow.id) && receipt.workflow.path === GH.workflowPath);
  requireSite(exact(receipt.run, ['id', 'number', 'attempt', 'url']) && positive(receipt.run.id) && positive(receipt.run.number)
    && positive(receipt.run.attempt) && receipt.run.url === `https://github.com/${SITE_GH.repository}/actions/runs/${receipt.run.id}`);
  validateCohort(receipt.cohort, plan.profile);
  requireSite(receipt.cohort.sourceRevision === receipt.source.measured && receipt.cohort.runId === receipt.run.id && receipt.cohort.attempt === receipt.run.attempt);
  requireSite(exact(receipt.artifacts, ['suite', 'provider']));
  for (const name of ['suite', 'provider']) validateArtifact(receipt.artifacts[name], SITE_GH[name], artifactLimit(SITE_GH[name]));
  validateAggregateJob(receipt.aggregateJob, receipt.run, receipt.artifacts);
  validateWorkers(receipt, plan);
  validateFiles(receipt.metadataFiles, null, 'metadata/');
  if (receipt.state === SITE_GH.archiveState) validateArchiveFields(receipt);
  else requireSite(receipt.archives === null && receipt.inputFiles === null);
  return receipt;
}

export const sameSiteIdentity = (left, right) => isDeepStrictEqual(left, right);
