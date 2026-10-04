import { validateAggregateProof } from './aggregate-proof.mjs';
import { GH, isolatedJobName, requireGitHub, timestamp } from './isolated-github-contract.mjs';
import { uniqueNamed, validateArtifact, validateSuccessfulJob, validateWorkerJob } from './isolated-github-validation.mjs';

export function selectCompletedEvidence(capture, context, plan) {
  const { jobs, artifacts, run } = capture;
  const groupedNames = new Set(plan.cells.map(cell => isolatedJobName(cell)));
  const grouped = jobs.some(job => groupedNames.has(job.name));
  const jobName = cell => grouped ? isolatedJobName(cell) : GH.casePrefix + cell.id;
  const targets = [...new Set(plan.cells.map(cell => cell.target))];
  const isWorker = job => job.name.startsWith(GH.casePrefix) || targets.some(target =>
    job.name.startsWith(target + ' / ') && !job.name.startsWith(target + ' / Check / '));
  const expectedJobs = new Set(plan.cells.map(jobName));
  const expectedArtifacts = new Set(plan.cells.map(cell => GH.artifactPrefix + cell.id));
  requireGitHub(jobs.filter(isWorker).length === plan.cells.length
    && jobs.every(job => !isWorker(job) || expectedJobs.has(job.name)));
  requireGitHub(artifacts.filter(item => item.name.startsWith(GH.artifactPrefix)).length === plan.cells.length
    && artifacts.every(item => !item.name.startsWith(GH.artifactPrefix) || expectedArtifacts.has(item.name)));
  const cells = plan.cells.map(cell => {
    const job = validateWorkerJob(uniqueNamed(jobs, jobName(cell)), context.cohort, jobName(cell));
    requireGitHub(timestamp(job.started_at) >= timestamp(run.run_started_at));
    const artifact = validateArtifact(uniqueNamed(artifacts, GH.artifactPrefix + cell.id), run, job,
      GH.artifactPrefix + cell.id, GH.workerZipBytes);
    return { cell, job, artifact };
  });
  requireGitHub(cells.reduce((total, item) => total + item.artifact.size_in_bytes, 0) <= GH.totalWorkerZipBytes);
  const job = validateSuccessfulJob(uniqueNamed(jobs, GH.imageJob), context.cohort, GH.imageJob, GH.imageSteps);
  requireGitHub(timestamp(job.started_at) >= timestamp(run.run_started_at));
  const artifact = validateArtifact(uniqueNamed(artifacts, GH.imageArtifact), run, job, GH.imageArtifact, GH.imageZipBytes);
  return { cells, image: { job, artifact } };
}

export const requireCompleteProof = (proof, plan) => validateAggregateProof(proof, plan);
