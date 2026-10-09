import { finalizeFailedResource } from './finalize-resource.mjs';
import { lstat, rename } from 'node:fs/promises';
import path from 'node:path';
import { AGGREGATE } from './aggregate-contracts.mjs';
import { DOCUMENT, selectedDocumentCell } from './document-isolated-plan.mjs';
import { validateDocumentEnvelope } from './document-evidence.mjs';
import { createDirectory, requireDirectory } from './image-bundle-files.mjs';
import { hashRegularFile, readJson, writeJson } from './isolated-github-files.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';

async function ownDirectory(root, parts) {
  let current = await requireDirectory(root);
  for (const part of parts) {
    const next = path.join(current, part);
    const info = await lstat(next).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
    current = info === null ? await createDirectory(next) : await requireDirectory(next);
  }
  return current;
}
export async function finalizeDocumentWorker({ workspace, environment, cohort, jobId, outcome }) {
  const cell = selectedDocumentCell(environment);
  requireGitHub(['success', 'failure'].includes(outcome) && Number.isSafeInteger(jobId) && jobId > 0);
  const directory = await ownDirectory(workspace, ['artifacts', 'comparisons', 'isolated', 'workers', cell.id]);
  const target = path.join(directory, DOCUMENT.raw);
  if (outcome === 'success') {
    const value = validateDocumentEnvelope(await readJson(target, GH.workerRawBytes), cell, cohort);
    requireGitHub(value.worker.jobId === jobId && value.disposition !== 'failed');
    return value;
  }
  const existing = await lstat(target).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (existing !== null) {
    await hashRegularFile(target, GH.workerRawBytes);
    const retained = await ownDirectory(workspace, ['artifacts', 'comparisons', 'isolated', 'failures', cell.id]);
    const backup = path.join(retained, 'failed-document-worker.json');
    requireGitHub(await lstat(backup).then(() => false, error => { if (error.code === 'ENOENT') return true; throw error; }));
    await rename(target, backup);
  }
  const value = { schemaVersion: 1, kind: DOCUMENT.kind, worker: { target: cell.target, nodeCount: cell.nodeCount,
    scenario: cell.scenario, profile: cell.profile, ...cohort, jobId },
    document: { scenario: cell.documentScenario, records: cell.documentRecords, clients: cell.documentClients },
    disposition: 'failed', reason: AGGREGATE.failureReason, report: null };
  validateDocumentEnvelope(value, cell, cohort);
  await writeJson(target, value);
  const retained = await ownDirectory(workspace, ['artifacts', 'comparisons', 'isolated', 'failures', cell.id]);
  await finalizeFailedResource({ directory, retained, cell, cohort, jobId, workerFile: DOCUMENT.raw });
  return value;
}
