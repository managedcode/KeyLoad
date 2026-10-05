import { validateAggregateProof } from './aggregate-proof.mjs';
import { GH, isolatedEvidenceJobName, requireGitHub, timestamp } from './isolated-github-contract.mjs';
import { uniqueNamed, validateArtifact, validateSuccessfulJob, validateWorkerJob } from './isolated-github-validation.mjs';
import { contextForProfile } from './isolated-github-context.mjs';
import { createScaledPlans } from './scaled-isolated-plan.mjs';

export function selectCompletedEvidence(capture, context, plan, scaledPlans = []) {
  const { jobs, artifacts, run } = capture;
  const hasScaleEvidence = jobs.some(job => / \/ scaled-(?:100k|1m|5m)-c16$/u.test(job.name)) ||
    artifacts.some(item => /-scaled-(?:100k|1m|5m)-c16$/u.test(item.name));
  const verifiedScales = scaledPlans.length > 0 ? scaledPlans : hasScaleEvidence ? createScaledPlans() : [];
  const profilePlans = [plan, ...verifiedScales];
  const cells = profilePlans.flatMap(item => item.cells);
  const groupedNames = new Set(cells.map(cell => isolatedEvidenceJobName(cell)));
  const grouped = jobs.some(job => groupedNames.has(job.name));
  const jobName = cell => grouped ? isolatedEvidenceJobName(cell) : GH.casePrefix + cell.id;
  const targets = [...new Set(cells.map(cell => cell.target))];
  const isWorker = job => job.name.startsWith(GH.casePrefix) || targets.some(target =>
    job.name.startsWith(target + ' / ') && !job.name.startsWith(target + ' / Check / '));
  const expectedJobs = new Set(cells.map(jobName));
  const expectedArtifacts = new Set(cells.map(cell => GH.artifactPrefix + cell.id));
  requireGitHub(jobs.filter(isWorker).length === cells.length
    && jobs.every(job => !isWorker(job) || expectedJobs.has(job.name)));
  requireGitHub(artifacts.filter(item => item.name.startsWith(GH.artifactPrefix)).length === cells.length
    && artifacts.every(item => !item.name.startsWith(GH.artifactPrefix) || expectedArtifacts.has(item.name)));
  const selectedCells = cells.map(cell => {
    const profileContext = contextForProfile(context, cell.profile);
    const job = validateWorkerJob(uniqueNamed(jobs, jobName(cell)), profileContext.cohort, jobName(cell));
    requireGitHub(timestamp(job.started_at) >= timestamp(run.run_started_at));
    const artifact = validateArtifact(uniqueNamed(artifacts, GH.artifactPrefix + cell.id), run, job,
      GH.artifactPrefix + cell.id, GH.workerZipBytes);
    return { cell, job, artifact, context: profileContext };
  });
  requireGitHub(selectedCells.reduce((total, item) => total + item.artifact.size_in_bytes, 0) <= GH.totalWorkerZipBytes);
  const job = validateSuccessfulJob(uniqueNamed(jobs, GH.imageJob), context.cohort, GH.imageJob, GH.imageSteps);
  requireGitHub(timestamp(job.started_at) >= timestamp(run.run_started_at));
  const artifact = validateArtifact(uniqueNamed(artifacts, GH.imageArtifact), run, job, GH.imageArtifact, GH.imageZipBytes);
  const returnedProfiles = new Set([plan.profile, ...scaledPlans.map(item => item.profile)]);
  return { cells: selectedCells.filter(item => returnedProfiles.has(item.cell.profile)), image: { job, artifact } };
}

export const requireCompleteProof = (proof, plan) => validateAggregateProof(proof, plan);
