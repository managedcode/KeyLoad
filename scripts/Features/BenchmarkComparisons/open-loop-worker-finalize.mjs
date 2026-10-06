import path from 'node:path';
import { link, lstat, unlink } from 'node:fs/promises';
import { isDeepStrictEqual } from 'node:util';
import { createDirectory, requireDirectory } from './image-bundle-files.mjs';
import { createDatabaseMatrices } from './isolated-preflight.mjs';
import { readIsolatedContract } from './isolated-plan.mjs';
import { createOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { CELL_TERMINAL } from './open-loop-cell-terminal-contract.mjs';
import { publishCellTerminal } from './open-loop-cell-terminal.mjs';
import { selectOpenLoopWorkload } from './open-loop-workload-selection.mjs';
import { contextForProfile } from './isolated-github-context.mjs';
import { GH, requireGitHub, positive } from './isolated-github-contract.mjs';

const WORKER_DIRECTORY = Object.freeze(['artifacts', 'comparisons', 'isolated', 'workers']);
const OUTCOMES = Object.freeze(['success', 'failure']);
const FAILED_OUTPUTS = Object.freeze([
  [CELL_TERMINAL.measurementFile, CELL_TERMINAL.reportBytes],
  [CELL_TERMINAL.proofFile, CELL_TERMINAL.reportBytes],
  [CELL_TERMINAL.sidecarFile, CELL_TERMINAL.sidecarBytes],
  [CELL_TERMINAL.genericWorkerFile, GH.workerRawBytes],
  [CELL_TERMINAL.genericSidecarFile, CELL_TERMINAL.sidecarBytes],
  ['.open-loop-evidence.v1.json.pending', CELL_TERMINAL.reportBytes],
  ['.open-loop-cancellation-proof.v1.json.pending', CELL_TERMINAL.reportBytes],
  ['.open-loop-server-resource-evidence.v1.json.pending', CELL_TERMINAL.sidecarBytes],
]);

export function resolveOpenLoopWorkerRow(context, environment) {
  const plan = context.openLoopPlan ?? createOpenLoopPlan();
  const cells = [...plan.measurementCells, ...plan.cancellationProofCells];
  const cell = cells.find(item => item.id === environment.KEYLOAD_COMPARISON_CELL_ID);
  requireGitHub(cell !== undefined);
  const matrices = createDatabaseMatrices(context.plan, context.scaledPlans, context.vectorPlans, plan);
  const rows = Object.values(matrices).flatMap(matrix => matrix.include)
    .filter(row => row.openLoopRate !== undefined);
  const row = rows.find(item => item.id === cell.id);
  requireGitHub(row !== undefined && row.scaleProfile === cell.profile
    && row.openLoopRate === cell.offeredRatePerSecond
    && row.openLoopCancellationProof === cell.cancellationProof
    && environment.KEYLOAD_OPEN_LOOP_RATE === String(cell.offeredRatePerSecond)
    && environment.KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF === String(cell.cancellationProof)
    && environment.Benchmarks__OpenLoopRate === undefined
    && environment.Benchmarks__OpenLoopCancellationProof === undefined
    && environment.KEYLOAD_COMPARISON_JOB_NAME === row.jobName
    && environment.KEYLOAD_SCALE_PROFILE === row.scaleProfile
    && (environment.KEYLOAD_VECTOR_PROFILE ?? '') === ''
    && (environment.Benchmarks__VectorProfile ?? '') === ''
    && environment.Benchmarks__EvidenceProfile === cell.profile
    && environment.Benchmarks__Target === cell.target
    && Number(environment.Benchmarks__NodeCount) === cell.nodeCount
    && environment.Benchmarks__Scenario === cell.scenario);
  const selected = selectOpenLoopWorkload(environment, row.scaleProfile, undefined);
  requireGitHub(isDeepStrictEqual(selected, cell));
  return { plan, cell, row };
}

export async function finalizeCurrentOpenLoopWorker({ workspace, context, environment, job, outcome }) {
  const { cell } = resolveOpenLoopWorkerRow(context, environment);
  const profileContext = contextForProfile(context, cell.profile);
  return await finalizeOpenLoopWorker({ workspace, context: profileContext, environment, job, outcome });
}

export async function finalizeOpenLoopWorker({ workspace, context, environment, job, outcome }) {
  const { plan, cell, row } = resolveOpenLoopWorkerRow(context, environment);
  requireGitHub(typeof workspace === 'string' && path.isAbsolute(workspace)
    && positive(job?.id) && job.name === row.jobName && job.run_id === context.cohort.runId
    && job.head_sha === context.cohort.sourceRevision
    && (!Object.hasOwn(job, 'run_attempt') || job.run_attempt === context.cohort.attempt)
    && OUTCOMES.includes(outcome));
  const cohort = context.cohort;
  requireGitHub(cohort.profile === cell.profile && cohort.sourceRevision === context.native.sourceSha
    && cohort.runId === Number(context.native.runId) && cohort.attempt === Number(context.native.runAttempt));
  const worker = { target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile,
    sourceRevision: cohort.sourceRevision, runId: cohort.runId, attempt: cohort.attempt,
    repository: cohort.repository, ref: cohort.ref, workflow: cohort.workflow, jobId: job.id };
  const unsupported = readIsolatedContract().unsupportedTopologies.find(item => item.target === cell.target
    && item.nodeCounts.includes(cell.nodeCount));
  const disposition = outcome === 'failure' ? 'failed'
    : unsupported ? 'unsupportedTopology' : cell.cancellationProof ? 'cancellationProof' : 'measured';
  const reason = disposition === 'failed' ? CELL_TERMINAL.failedReason
    : disposition === 'unsupportedTopology' ? unsupported.reason : null;
  const directory = await ensureDirectory(workspace, [...WORKER_DIRECTORY, cell.id]);
  if (disposition === 'failed') await retainFailedOutputs(workspace, cell.id, directory);
  return await publishCellTerminal({ directory, plan, cell, worker, disposition, reason });
}

async function retainFailedOutputs(workspace, cellId, workerDirectory) {
  const failureDirectory = await ensureDirectory(workspace,
    ['artifacts', 'comparisons', 'isolated', 'failures', cellId]);
  for (const [name, maximumBytes] of FAILED_OUTPUTS) {
    const source = path.join(workerDirectory, name);
    const existing = await lstat(source).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    if (existing === null) continue;
    requireGitHub(existing.isFile() && !existing.isSymbolicLink() && existing.size <= maximumBytes);
    const target = path.join(failureDirectory, name);
    await link(source, target);
    await unlink(source);
  }
}

async function ensureDirectory(root, components) {
  let directory = await requireDirectory(root);
  for (const component of components) {
    const next = path.join(directory, component);
    const existing = await lstat(next).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    directory = existing === null ? await createDirectory(next) : await requireDirectory(next);
  }
  return directory;
}
