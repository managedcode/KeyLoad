import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { openExclusive, hashRegularFile } from './isolated-github-files.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { responseDecoder } from './isolated-github-headers.mjs';

function stopOwned(child, state) {
  if (state.stopping) return;
  state.stopping = true;
  child.kill('SIGTERM');
  state.force = setTimeout(() => child.kill('SIGKILL'), 1000);
}

function monitor(child, state, timeoutMs) {
  state.timeout = setTimeout(() => { state.timedOut = true; stopOwned(child, state); }, timeoutMs);
  child.stderr.on('data', chunk => {
    state.stderrBytes += chunk.length;
    if (state.stderrBytes > GH.stderrBytes) { state.exceeded = true; stopOwned(child, state); }
  });
  return new Promise(resolve => {
    child.once('error', () => { state.spawnFailed = true; });
    child.once('close', (code, signal) => resolve({ code, signal }));
  });
}

async function copyBounded(child, handle, maximumBytes, state, decoder) {
  const hash = createHash('sha256');
  let bytes = 0;
  for await (const nativeChunk of child.stdout) {
    const chunk = decoder ? await decoder.decode(nativeChunk) : nativeChunk;
    bytes += chunk.length;
    const limit = decoder?.response?.status === 200 ? maximumBytes : decoder ? GH.metadataBytes : maximumBytes;
    if (bytes > limit) { state.exceeded = true; stopOwned(child, state); requireGitHub(false); }
    await handle.writeFile(chunk);
    hash.update(chunk);
  }
  return { bytes, sha256: hash.digest('hex') };
}

export async function streamToFile(command, argv, target, maximumBytes, timeoutMs, cwd, signal) {
  return captureStream(command, argv, target, maximumBytes, timeoutMs, cwd, undefined, signal);
}

export async function streamHttpToFile(command, argv, target, headers, maximumBytes, timeoutMs, cwd, signal) {
  return captureStream(command, argv, target, maximumBytes, timeoutMs, cwd, responseDecoder(headers), signal);
}

async function captureStream(command, argv, target, maximumBytes, timeoutMs, cwd, decoder, signal) {
  requireGitHub(Number.isSafeInteger(maximumBytes) && maximumBytes > 0 && Number.isSafeInteger(timeoutMs) && timeoutMs > 0);
  signal?.throwIfAborted();
  const handle = await openExclusive(target);
  let child;
  try {
    signal?.throwIfAborted();
    child = spawn(command, argv, { cwd, shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
      env: { ...process.env, NO_COLOR: '1', CLICOLOR_FORCE: '0', GH_FORCE_TTY: '' } });
  } catch { await handle.close(); throw new Error(GH.failure); }
  const state = { timedOut: false, exceeded: false, spawnFailed: false, stopping: false, stderrBytes: 0 };
  const cancel = () => { state.cancelled = true; stopOwned(child, state); };
  signal?.addEventListener('abort', cancel, { once: true });
  process.on('SIGTERM', cancel);
  process.on('SIGINT', cancel);
  if (signal?.aborted) cancel();
  const exit = monitor(child, state, timeoutMs);
  try {
    const output = await copyBounded(child, handle, maximumBytes, state, decoder);
    const result = await exit;
    requireGitHub(result.signal === null && !state.timedOut && !state.exceeded && !state.spawnFailed && !state.cancelled);
    requireGitHub(decoder ? decoder.response !== undefined && [0, 1].includes(result.code) : result.code === 0);
    await handle.sync();
    return { ...output, ...(decoder ? { response: decoder.response, exitCode: result.code } : {}) };
  } finally {
    if (child.exitCode === null) stopOwned(child, state);
    await exit;
    clearTimeout(state.timeout);
    clearTimeout(state.force);
    signal?.removeEventListener('abort', cancel);
    process.off('SIGTERM', cancel);
    process.off('SIGINT', cancel);
    await handle.chmod(0o400);
    await handle.close();
  }
}

export async function validateDownloadedArchive(target, artifact, maximumBytes) {
  const actual = await hashRegularFile(target, maximumBytes);
  requireGitHub(actual.bytes === artifact.size_in_bytes && `sha256:${actual.sha256}` === artifact.digest);
  return actual;
}
