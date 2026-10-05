import { link, unlink } from 'node:fs/promises';
import path from 'node:path';
import { setTimeout as wait } from 'node:timers/promises';
import { runBounded } from './image-process.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { writeCapture, writeJson } from './isolated-github-files.mjs';
import { streamHttpToFile } from './isolated-github-stream.mjs';
import { retryDelay } from './isolated-github-rate.mjs';

export async function initializeTransport(context, root, directory) {
  const requests = await createDirectory(path.join(directory, 'requests'));
  const help = await runBounded('gh', ['api', '--help'], { cwd: context.native.workspace,
    timeoutMs: GH.metadataTimeoutMs, maximumOutputBytes: GH.stderrBytes });
  requireGitHub(help.success);
  await writeCapture(path.join(directory, 'gh-api-help.txt'), Buffer.from(help.stdout));
  context.transport = { root, requests, sequence: 0, waitedMs: 0, allowBinary: help.stdout.includes('--allow-escape-sequences') };
}

export function nativeArguments(endpoint, context, currentJobId) {
  const args = ['api', '--hostname', GH.host, '--method', 'GET', '--include',
    '-H', 'Accept: application/vnd.github+json', '-H', `X-GitHub-Api-Version: ${GH.apiVersion}`];
  if (currentJobId !== undefined) {
    requireGitHub(Number.isSafeInteger(currentJobId) && currentJobId > 0
      && endpoint === `${GH.api}/jobs/${currentJobId}`);
    args.push('-H', 'Cache-Control: no-cache, max-age=0');
  }
  args.push(endpoint);
  if (context.transport.allowBinary) args.push('--allow-escape-sequences');
  return args;
}

function remainingMilliseconds(deadline) {
  return Number((deadline - process.hrtime.bigint()) / 1000000n);
}

export async function requestNative(endpoint, target, maximumBytes, timeoutMs, context, options = {}) {
  requireGitHub(endpoint.startsWith(GH.api + '/') && context.transport !== undefined);
  requireGitHub(options.deadline === undefined || typeof options.deadline === 'bigint');
  requireGitHub(options.signal === undefined || typeof options.signal.aborted === 'boolean');
  requireGitHub(options.currentJobId === undefined || Number.isSafeInteger(options.currentJobId));
  const state = context.transport;
  for (let repeat = 0; repeat <= GH.rateRepeats; repeat += 1) {
    options.signal?.throwIfAborted();
    const remaining = options.deadline === undefined ? timeoutMs : remainingMilliseconds(options.deadline);
    requireGitHub(remaining > 0);
    const requestTimeout = Math.min(timeoutMs, remaining);
    state.sequence += 1;
    const name = `request-${String(state.sequence).padStart(5, '0')}`;
    const body = path.join(state.requests, `${name}.body`);
    const headers = path.join(state.requests, `${name}.headers`);
    const result = await streamHttpToFile('gh', nativeArguments(endpoint, context, options.currentJobId), body, headers,
      maximumBytes, requestTimeout, context.native.workspace, options.signal);
    const successful = result.response.status === 200 && result.exitCode === 0;
    let delay = 0;
    try {
      if (!successful) delay = retryDelay(result.response, Date.now(), state.waitedMs, repeat);
    } finally {
      await writeJson(path.join(state.requests, `${name}.json`), { endpoint, repeat, status: result.response.status,
        body: path.relative(state.root, successful ? target : body), headers: path.relative(state.root, headers),
        bytes: result.bytes, sha256: result.sha256, waitMs: delay, plannedAccumulatedWaitMs: state.waitedMs + delay });
    }
    if (successful) {
      await link(body, target);
      await unlink(body);
      options.signal?.throwIfAborted();
      requireGitHub(options.deadline === undefined || remainingMilliseconds(options.deadline) > 0);
      return result;
    }
    options.signal?.throwIfAborted();
    const startedAt = new Date().toISOString();
    const started = process.hrtime.bigint();
    const available = options.deadline === undefined ? Number.POSITIVE_INFINITY : remainingMilliseconds(options.deadline);
    requireGitHub(delay <= available && available > 0);
    await wait(delay, undefined, { signal: options.signal });
    const elapsedMs = Math.ceil(Number(process.hrtime.bigint() - started) / 1000000);
    state.waitedMs += elapsedMs;
    await writeJson(path.join(state.requests, `${name}.wait.json`), { endpoint, requestedMs: delay,
      elapsedMs, accumulatedWaitMs: state.waitedMs, startedAt, completedAt: new Date().toISOString() });
    requireGitHub(state.waitedMs <= GH.rateWaitMs);
  }
  requireGitHub(false);
}
