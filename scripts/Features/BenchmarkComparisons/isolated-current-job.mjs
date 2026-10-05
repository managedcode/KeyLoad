import path from 'node:path';
import { setTimeout as wait } from 'node:timers/promises';
import { captureApi } from './isolated-github-api.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { validateJobIdentity } from './isolated-github-validation.mjs';

const refreshCaptures = 61;
const refreshDelayMs = 1000;
const queuedFreshnessWindowMs = 60000;

function createCancellationScope() {
  const controller = new AbortController();
  const cancel = () => controller.abort();
  process.on('SIGTERM', cancel);
  process.on('SIGINT', cancel);
  return {
    signal: controller.signal,
    dispose: () => {
      process.off('SIGTERM', cancel);
      process.off('SIGINT', cancel);
    },
  };
}

function remainingMilliseconds(deadline) {
  return Number((deadline - process.hrtime.bigint()) / 1000000n);
}

export function waitForRefresh(signal, remaining) {
  requireGitHub(remaining > 0);
  return wait(Math.min(refreshDelayMs, remaining), undefined, { signal });
}

export function classifyCurrentJob(job, cohort, name, discoveredId) {
  validateJobIdentity(job, cohort, name);
  requireGitHub(job.id === discoveredId && job.workflow_name === GH.workflow && job.head_branch === 'main');

  if (job.status === 'in_progress' && job.conclusion === null) return 'running';
  if (job.status === 'queued' && job.conclusion === null) return 'queued';
  requireGitHub(false);
}

export async function captureFreshCurrentJob(discovered, directory, context, name) {
  classifyCurrentJob(discovered, context.cohort, name, discovered.id);
  const cancellation = createCancellationScope();
  let deadline;
  try {
    for (let capture = 1; capture <= refreshCaptures; capture += 1) {
      cancellation.signal.throwIfAborted();
      const remaining = deadline === undefined ? GH.metadataTimeoutMs : remainingMilliseconds(deadline);
      requireGitHub(remaining > 0);
      if (capture > 1) {
        await waitForRefresh(cancellation.signal, remaining);
      }

      cancellation.signal.throwIfAborted();
      const requestRemaining = deadline === undefined ? GH.metadataTimeoutMs : remainingMilliseconds(deadline);
      requireGitHub(requestRemaining > 0);
      const target = path.join(directory, `current-job-refresh-${String(capture).padStart(2, '0')}.json`);
      const fresh = await captureApi(`${GH.api}/jobs/${discovered.id}`, target, false, context, {
        currentJobId: discovered.id,
        deadline,
        signal: cancellation.signal,
      });
      cancellation.signal.throwIfAborted();
      const state = classifyCurrentJob(fresh, context.cohort, name, discovered.id);
      if (state === 'running') return fresh;
      if (deadline === undefined) {
        deadline = process.hrtime.bigint() + BigInt(queuedFreshnessWindowMs) * 1000000n;
      }
    }

    requireGitHub(false);
  } finally {
    cancellation.dispose();
  }
}
