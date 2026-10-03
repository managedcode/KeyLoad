import { performance } from 'node:perf_hooks';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { requireNative } from './native-serialization-contract.mjs';
import { writeJson } from './native-serialization-files.mjs';
import { captureGitHub, requiredStepsReady, requireCompletedSteps } from './native-serialization-github.mjs';

export const VISIBILITY_DEADLINE_MS = 120000;
export const VISIBILITY_INTERVAL_MS = 10000;

export function visibilityWait(start, now) {
  requireNative(Number.isFinite(start) && start >= 0 && Number.isFinite(now) && now >= start, 'github.visibilityClock');
  const remaining = VISIBILITY_DEADLINE_MS - (now - start);
  requireNative(remaining > 0, 'github.visibilityDeadline');
  return Math.min(VISIBILITY_INTERVAL_MS, remaining);
}

export async function captureReadyGitHub(directory, identity, measured = false, expectedJobId) {
  const start = performance.now();
  const controller = new AbortController();
  const deadline = setTimeout(() => controller.abort(new Error('Invalid native-serialization evidence: github.visibilityDeadline.')),
    VISIBILITY_DEADLINE_MS);
  const phase = measured ? 'verify' : 'prepare';
  let jobId = expectedJobId;
  try {
    for (let attempt = 1; ; attempt++) {
      controller.signal.throwIfAborted();
      visibilityWait(start, performance.now());
      const inspection = `github-${phase}-inspection-${String(attempt).padStart(4, '0')}`;
      const github = await captureGitHub(identity, controller.signal,
        async (part, response, route) => {
          await writeJson(join(directory, `${inspection}-${part}.json`), { route, response });
          controller.signal.throwIfAborted();
          visibilityWait(start, performance.now());
        });
      await writeJson(join(directory, `${inspection}.json`), github);
      jobId ??= github.job.id;
      requireNative(github.job.id === jobId, 'github.jobChanged');
      controller.signal.throwIfAborted();
      visibilityWait(start, performance.now());
      if (requiredStepsReady(github.job, measured)) {
        await writeJson(join(directory, `github-${phase}.json`), github);
        controller.signal.throwIfAborted();
        visibilityWait(start, performance.now());
        requireCompletedSteps(github.job, measured);
        return github;
      }
      await delay(visibilityWait(start, performance.now()), undefined, { signal: controller.signal });
    }
  } finally { clearTimeout(deadline); }
}
