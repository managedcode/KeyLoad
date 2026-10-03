import path from 'node:path';
import { setTimeout as wait } from 'node:timers/promises';
import { captureApi } from './isolated-github-api.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { validateJobIdentity } from './isolated-github-validation.mjs';

const refreshCaptures = 3;
const refreshDelayMs = 1000;

export function classifyCurrentJob(job, cohort, name, discoveredId) {
  validateJobIdentity(job, cohort, name);
  requireGitHub(job.id === discoveredId && job.workflow_name === GH.workflow && job.head_branch === 'main');

  if (job.status === 'in_progress' && job.conclusion === null) return 'running';
  if (job.status === 'queued' && job.conclusion === null) return 'queued';
  requireGitHub(false);
}

export async function captureFreshCurrentJob(discovered, directory, context, name) {
  classifyCurrentJob(discovered, context.cohort, name, discovered.id);

  for (let capture = 1; capture <= refreshCaptures; capture += 1) {
    const target = path.join(directory, `current-job-refresh-${String(capture).padStart(2, '0')}.json`);
    const fresh = await captureApi(`${GH.api}/jobs/${discovered.id}`, target, false, context);
    const state = classifyCurrentJob(fresh, context.cohort, name, discovered.id);
    if (state === 'running') return fresh;
    if (capture < refreshCaptures) await wait(refreshDelayMs);
  }

  requireGitHub(false);
}
