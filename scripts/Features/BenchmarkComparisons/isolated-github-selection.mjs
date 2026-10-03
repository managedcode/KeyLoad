import { validateAggregateProof } from './aggregate-proof.mjs';
import { GH, requireGitHub, timestamp } from './isolated-github-contract.mjs';
import { uniqueNamed, validateArtifact, validateSuccessfulJob } from './isolated-github-validation.mjs';

export function selectCompletedEvidence(capture, context, plan) {
  const { jobs, artifacts, run } = capture;
  const expectedJobs = new Set(plan.cells.map(cell => GH.casePrefix + cell.id));
  const expectedArtifacts = new Set(plan.cells.map(cell => GH.artifactPrefix + cell.id));
  requireGitHub(jobs.filter(job => job.name.startsWith(GH.casePrefix)).length === plan.cells.length
    && jobs.every(job => !job.name.startsWith(GH.casePrefix) || expectedJobs.has(job.name)));
  requireGitHub(artifacts.filter(item => item.name.startsWith(GH.artifactPrefix)).length === plan.cells.length
    && artifacts.every(item => !item.name.startsWith(GH.artifactPrefix) || expectedArtifacts.has(item.name)));
  const cells = plan.cells.map(cell => {
    const job = validateSuccessfulJob(uniqueNamed(jobs, GH.casePrefix + cell.id), context.cohort,
      GH.casePrefix + cell.id, GH.workerSteps);
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
