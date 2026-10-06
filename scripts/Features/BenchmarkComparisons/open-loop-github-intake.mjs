import { createDatabaseMatrices } from './isolated-preflight.mjs';
import { contextForProfile } from './isolated-github-context.mjs';
import { GH } from './isolated-github-contract.mjs';
import { projectArtifact, projectJob, uniqueNamed, validateArtifact, validateWorkerJob } from './isolated-github-validation.mjs';
import { CELL_TERMINAL, positive, reject } from './open-loop-cell-terminal-contract.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { AGGREGATE } from './aggregate-contracts.mjs';

const INTAKE_KIND = 'open-loop-cohort-intake.v1';
const OPEN_LOOP_JOB = / \/ (?:open-loop|cancellation proof)\b/u;
const OPEN_LOOP_ARTIFACT = /^comparison-open-loop-/u;

export function selectOpenLoopWorkers(capture, context, inputPlan) {
  const plan = validateOpenLoopPlan(inputPlan);
  const rows = openLoopRows(context, plan);
  const jobs = capture.jobs.filter(job => OPEN_LOOP_JOB.test(job.name));
  const artifacts = capture.artifacts.filter(artifact => OPEN_LOOP_ARTIFACT.test(artifact.name));
  reject(jobs.length === rows.length && artifacts.length === rows.length);
  let archiveBytes = 0;
  const selected = rows.map(row => {
    const profileContext = contextForProfile(context, row.profile);
    const job = validateWorkerJob(uniqueNamed(jobs, row.jobName), profileContext.cohort, row.jobName);
    validateCellJobStart(job, capture.run, profileContext);
    const name = row.artifactPrefix + row.id;
    const artifact = validateArtifact(uniqueNamed(artifacts, name), capture.run, job, name, GH.workerZipBytes);
    archiveBytes += artifact.size_in_bytes;
    reject(Number.isSafeInteger(archiveBytes) && archiveBytes <= CELL_TERMINAL.totalBytes);
    return { cell: row, job, artifact, context: profileContext };
  });
  reject(new Set(selected.map(item => item.job.id)).size === rows.length
    && new Set(selected.map(item => item.artifact.id)).size === rows.length);
  return { plan, selected, archiveBytes, jobIds: new Set(selected.map(item => item.job.id)) };
}

export function createOpenLoopIntakeHeader(context, plan, planBytes) {
  reject(Buffer.isBuffer(planBytes) && planBytes.length <= AGGREGATE.metadataBytes);
  return { planBytes, plan, intake: { schemaVersion: 1, kind: INTAKE_KIND,
    cohort: cohortForIntake(context), cells: [] } };
}

export function projectOpenLoopIntakeCell(item) {
  return { id: item.cell.id, job: projectJob(item.job, item.context.cohort, CELL_TERMINAL.measurementSteps),
    artifact: projectArtifact(item.artifact) };
}

function openLoopRows(context, plan) {
  const matrices = createDatabaseMatrices(context.plan, context.scaledPlans, context.vectorPlans, plan);
  const rows = Object.values(matrices).flatMap(matrix => matrix.include)
    .filter(row => row.openLoopRate !== undefined);
  const expected = [...plan.measurementCells, ...plan.cancellationProofCells];
  reject(rows.length === CELL_TERMINAL.maximumCells && new Set(rows.map(row => row.id)).size === rows.length);
  const byId = new Map(rows.map(row => [row.id, row]));
  return expected.map(cell => {
    const row = byId.get(cell.id);
    reject(row !== undefined && row.openLoopRate === cell.offeredRatePerSecond
      && row.openLoopCancellationProof === cell.cancellationProof && row.profile === cell.profile);
    return row;
  });
}

function validateCellJobStart(job, run, context) {
  reject(job.head_sha === context.cohort.sourceRevision && job.run_id === context.cohort.runId
    && (!Object.hasOwn(job, 'run_attempt') || job.run_attempt === context.cohort.attempt)
    && Date.parse(job.started_at) >= Date.parse(run.run_started_at));
}

function cohortForIntake(context) {
  const { sourceRevision, runId, attempt, repository, ref, workflow } = context.cohort;
  reject(positive(runId) && positive(attempt));
  return { sourceRevision, runId, attempt, repository, ref, workflow };
}
