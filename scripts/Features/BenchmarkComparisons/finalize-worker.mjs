import { lstat, rename } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { validateWorkerEnvelope } from './aggregate-validation.mjs';
import { createIsolatedPlan, readIsolatedContract } from './isolated-plan.mjs';
import { createDirectory, requireDirectory } from './image-bundle-files.mjs';
import { createGitHubContext } from './isolated-github-context.mjs';
import { captureCurrentJob } from './isolated-github-job.mjs';
import { GH, isolatedJobName, positive, requireGitHub } from './isolated-github-contract.mjs';
import { hashRegularFile, readJson, writeJson } from './isolated-github-files.mjs';

const FINAL = Object.freeze({ workers: ['artifacts', 'comparisons', 'isolated', 'workers'],
  failures: ['artifacts', 'comparisons', 'isolated', 'failures'], raw: 'worker.json', retained: 'failed-worker.json',
  outcome: 'KEYLOAD_WORKLOAD_OUTCOME', identity: 'KEYLOAD_COMPARISON_JOB_ID', cell: 'KEYLOAD_COMPARISON_CELL_ID' });

async function ensureDirectory(root, components) {
  let directory = await requireDirectory(root);
  for (const component of components) {
    const next = path.join(directory, component);
    const existing = await lstat(next).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    directory = existing ? await requireDirectory(next) : await createDirectory(next);
  }
  return directory;
}

// A parser/filesystem contract; authentic job agreement is checked by the GitHub collector.
export async function finalizeWorker({ workspace, cell, cohort, jobId, outcome }) {
  requireValue(cell !== undefined && isDeepStrictEqual(createIsolatedPlan().cells.find(item => item.id === cell.id), cell),
    AGGREGATE.errors.envelope);
  requireValue(positive(jobId) && [AGGREGATE.success, AGGREGATE.failure].includes(outcome), AGGREGATE.errors.envelope);
  validateCohort(cohort, cell.profile);
  const directory = await ensureDirectory(workspace, [...FINAL.workers, cell.id]);
  const target = path.join(directory, FINAL.raw);
  if (outcome === AGGREGATE.success) {
    const value = validateWorkerEnvelope(await readJson(target, GH.workerRawBytes), cell, cohort, readIsolatedContract());
    requireValue(value.worker.jobId === jobId && value.disposition !== AGGREGATE.failed, AGGREGATE.errors.envelope);
    return value;
  }
  const value = { schemaVersion: AGGREGATE.version, worker: {
    target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile, ...cohort, jobId },
    disposition: AGGREGATE.failed, reason: AGGREGATE.failureReason, report: null };
  validateWorkerEnvelope(value, cell, cohort, readIsolatedContract());
  const existing = await lstat(target).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (existing) {
    await hashRegularFile(target, GH.workerRawBytes);
    const retained = await ensureDirectory(workspace, [...FINAL.failures, cell.id]);
    const backup = path.join(retained, FINAL.retained);
    requireValue(await lstat(backup).catch(error => { if (error.code === 'ENOENT') return null; throw error; }) === null,
      AGGREGATE.errors.output);
    await rename(target, backup);
  }
  await writeJson(target, value);
  return value;
}

export async function finalizeCurrentWorker(environment = process.env, argv = process.argv.slice(2)) {
  requireGitHub(argv.length === 0);
  const context = createGitHubContext(environment, process.platform);
  const cell = context.plan.cells.find(item => item.id === environment[FINAL.cell]);
  requireGitHub(cell !== undefined && [isolatedJobName(cell), isolatedJobName(cell, true)]
    .includes(environment.KEYLOAD_COMPARISON_JOB_NAME));
  let jobId = Number(environment[FINAL.identity]);
  if (!positive(jobId)) jobId = (await captureCurrentJob(environment, [], true)).id;
  return finalizeWorker({ workspace: context.native.workspace, cell, cohort: context.cohort, jobId,
    outcome: environment[FINAL.outcome] });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await finalizeCurrentWorker(); } catch { process.stderr.write(`${GH.failure}\n`); process.exitCode = 1; }
}
