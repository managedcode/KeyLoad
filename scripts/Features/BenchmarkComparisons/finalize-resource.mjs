import path from 'node:path';
import { lstat, rename } from 'node:fs/promises';
import { hashRegularFile, writeJson } from './isolated-github-files.mjs';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';
import { SERVER_RESOURCE_MISSING_KINDS } from './server-resource-evidence.mjs';

// Failed preparation/execution has no publishable resource measurements. Preserve any original sidecar in diagnostics.
export async function finalizeFailedResource({ directory, retained, cell, cohort, jobId, workerFile = 'worker.json' }) {
  if (cell.profile === 'intensive-1k-c16') return;
  const file = path.join(directory, 'server-resource-evidence.json');
  const original = await lstat(file).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (original) {
    await hashRegularFile(file, 65_536);
    const backup = path.join(retained, 'failed-server-resource-evidence.json');
    requireValue(await lstat(backup).catch(error => { if (error.code === 'ENOENT') return null; throw error; }) === null,
      AGGREGATE.errors.output);
    await rename(file, backup);
  }
  const worker = await hashRegularFile(path.join(directory, workerFile), AGGREGATE.workerBytes);
  await writeJson(file, { schema: 'server-resource-evidence.v1', sourceRevision: cohort.sourceRevision,
    workflowRunId: String(cohort.runId), runAttempt: String(cohort.attempt), jobId: String(jobId), target: cell.target,
    nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile, workerSha256: worker.sha256,
    hardware: null, appHostEnvelope: null, containers: [], missingEvidence: [...SERVER_RESOURCE_MISSING_KINDS], qualified: false });
}
