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

function nativeArguments(endpoint, context) {
  const args = ['api', '--hostname', GH.host, '--method', 'GET', '--include',
    '-H', 'Accept: application/vnd.github+json', '-H', `X-GitHub-Api-Version: ${GH.apiVersion}`, endpoint];
  if (context.transport.allowBinary) args.push('--allow-escape-sequences');
  return args;
}

export async function requestNative(endpoint, target, maximumBytes, timeoutMs, context) {
  requireGitHub(endpoint.startsWith(GH.api + '/') && context.transport !== undefined);
  const state = context.transport;
  for (let repeat = 0; repeat <= GH.rateRepeats; repeat += 1) {
    state.sequence += 1;
    const name = `request-${String(state.sequence).padStart(5, '0')}`;
    const body = path.join(state.requests, `${name}.body`);
    const headers = path.join(state.requests, `${name}.headers`);
    const result = await streamHttpToFile('gh', nativeArguments(endpoint, context), body, headers,
      maximumBytes, timeoutMs, context.native.workspace);
    const successful = result.response.status === 200 && result.exitCode === 0;
    let delay = 0;
    try {
      if (!successful) delay = retryDelay(result.response, Date.now(), state.waitedMs, repeat);
    } finally {
      await writeJson(path.join(state.requests, `${name}.json`), { endpoint, repeat, status: result.response.status,
        body: path.relative(state.root, successful ? target : body), headers: path.relative(state.root, headers),
        bytes: result.bytes, sha256: result.sha256, waitMs: delay, plannedAccumulatedWaitMs: state.waitedMs + delay });
    }
    if (successful) { await link(body, target); await unlink(body); return result; }
    const startedAt = new Date().toISOString();
    const started = process.hrtime.bigint();
    await wait(delay);
    const elapsedMs = Math.ceil(Number(process.hrtime.bigint() - started) / 1000000);
    state.waitedMs += elapsedMs;
    await writeJson(path.join(state.requests, `${name}.wait.json`), { endpoint, requestedMs: delay,
      elapsedMs, accumulatedWaitMs: state.waitedMs, startedAt, completedAt: new Date().toISOString() });
    requireGitHub(state.waitedMs <= GH.rateWaitMs);
  }
  requireGitHub(false);
}
