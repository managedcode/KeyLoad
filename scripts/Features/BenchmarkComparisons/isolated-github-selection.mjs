import { createDocumentPlan, documentJobName, DOCUMENT } from './document-isolated-plan.mjs';
import { validateAggregateProof } from './aggregate-proof.mjs';
import { GH, isolatedEvidenceJobName, requireGitHub, timestamp } from './isolated-github-contract.mjs';
import { uniqueNamed, validateArtifact, validateSuccessfulJob, validateWorkerJob } from './isolated-github-validation.mjs';
import { contextForProfile } from './isolated-github-context.mjs';
import { createScaledPlans } from './scaled-isolated-plan.mjs';
import { createVectorPlans } from './vector-isolated-plan.mjs';
import { createDatabaseMatrices } from './isolated-preflight.mjs';

export function selectCompletedEvidence(capture, context, plan, scaledPlans = [], vectorPlans = []) {
  const { jobs, artifacts, run } = capture;
  const hasScaleEvidence = jobs.some(job => / \/ scaled-(?:100k|1m)-c16$/u.test(job.name)) ||
    artifacts.some(item => /-scaled-(?:100k|1m)-c16$/u.test(item.name));
  const verifiedScales = scaledPlans.length > 0 ? scaledPlans : hasScaleEvidence ? createScaledPlans() : [];
  const hasVectorEvidence = jobs.some(job => / \/ vector-(?:100k|1m)-(?:exact|hnsw|ivfflat|native)-(?:plain|filtered|mixed)-c16$/u.test(job.name)) ||
    artifacts.some(item => /-vector-(?:100k|1m)-(?:exact|hnsw|ivfflat|native)-(?:plain|filtered|mixed)-c16$/u.test(item.name));
  const verifiedVectors = vectorPlans.length > 0 ? vectorPlans : hasVectorEvidence ? createVectorPlans() : [];
  const profilePlans = [plan, ...verifiedScales, ...verifiedVectors];
  const cells = profilePlans.flatMap(item => item.cells);
  const openLoopRows = context.openLoopPlan === undefined ? []
    : Object.values(createDatabaseMatrices(plan, verifiedScales, verifiedVectors, context.openLoopPlan))
      .flatMap(matrix => matrix.include).filter(row => row.openLoopRate !== undefined);
  const openLoopJobNames = new Set(openLoopRows.map(row => row.jobName));
  const openLoopArtifactNames = new Set(openLoopRows.map(row => row.artifactPrefix + row.id));
  const documentCells = createDocumentPlan().cells;
  const documentJobs = new Set(documentCells.map(documentJobName));
  const regularJobs = jobs.filter(job => !openLoopJobNames.has(job.name) && !documentJobs.has(job.name));
  const regularArtifacts = artifacts.filter(item => !openLoopArtifactNames.has(item.name) && !item.name.startsWith(DOCUMENT.artifactPrefix));
  const jobName = cell => isolatedEvidenceJobName(cell);
  const targets = [...new Set(cells.map(cell => cell.target))];
  const isWorker = job => job.name.startsWith(GH.casePrefix) || targets.some(target =>
    job.name.startsWith(target + ' / ') && !job.name.startsWith(target + ' / Check / '));
  const expectedJobs = new Set(cells.map(jobName));
  const expectedArtifacts = new Set(cells.map(cell => GH.artifactPrefix + cell.id));
  requireGitHub(regularJobs.filter(isWorker).length === cells.length
    && regularJobs.every(job => !isWorker(job) || expectedJobs.has(job.name)));
  requireGitHub(regularArtifacts.filter(item => item.name.startsWith(GH.artifactPrefix)).length === cells.length
    && regularArtifacts.every(item => !item.name.startsWith(GH.artifactPrefix) || expectedArtifacts.has(item.name)));
  const selectedCells = cells.map(cell => {
    const profileContext = contextForProfile(context, cell.profile);
    const job = validateWorkerJob(uniqueNamed(regularJobs, jobName(cell)), profileContext.cohort, jobName(cell));
    requireGitHub(timestamp(job.started_at) >= timestamp(run.run_started_at));
    const artifact = validateArtifact(uniqueNamed(regularArtifacts, GH.artifactPrefix + cell.id), run, job,
      GH.artifactPrefix + cell.id, GH.workerZipBytes);
    return { cell, job, artifact, context: profileContext };
  });
  requireGitHub(selectedCells.reduce((total, item) => total + item.artifact.size_in_bytes, 0) <= GH.totalWorkerZipBytes);
  const job = validateSuccessfulJob(uniqueNamed(jobs, GH.imageJob), context.cohort, GH.imageJob, GH.imageSteps);
  requireGitHub(timestamp(job.started_at) >= timestamp(run.run_started_at));
  const artifact = validateArtifact(uniqueNamed(artifacts, GH.imageArtifact), run, job, GH.imageArtifact, GH.imageZipBytes);
  const returnedProfiles = new Set([plan.profile, ...verifiedScales.map(item => item.profile), ...verifiedVectors.map(item => item.profile)]);
  return { cells: selectedCells.filter(item => returnedProfiles.has(item.cell.profile)), image: { job, artifact } };
}

export const requireCompleteProof = (proof, plan) => validateAggregateProof(proof, plan);
