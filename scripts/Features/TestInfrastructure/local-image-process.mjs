import { spawn } from 'node:child_process';
import { localImage, messages } from './local-image-contracts.mjs';

let stopping = false;
let active = null;

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.once(signal, () => {
    stopping = true;
    if (active) {
      try { terminate(active, signal); } catch { /* Preserve signal settlement. */ }
    }
  });
}

export async function runDocker(root, args, timeoutMs, capture = true) {
  if (stopping) throw new Error(messages.timeout);
  const child = spawn(localImage.docker, args, {
    cwd: root,
    detached: process.platform !== 'win32',
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });
  active = child;
  const stdout = drain(child.stdout, capture, child);
  const stderr = drain(child.stderr, capture, child);
  let timedOut = false;
  let killGrace;
  const deadline = setTimeout(() => {
    timedOut = true;
    terminate(child, 'SIGTERM');
    killGrace = setTimeout(() => terminate(child, 'SIGKILL'), localImage.killGraceMs);
    killGrace.unref();
  }, timeoutMs);
  const result = await new Promise(resolve => {
    let spawnError;
    child.once('error', error => { spawnError = error; });
    child.once('close', (code, signal) => resolve({ code, signal, error: spawnError }));
  }).finally(() => {
    clearTimeout(deadline);
    if (killGrace) clearTimeout(killGrace);
  });
  const [out, err] = await Promise.all([stdout, stderr]);
  if (active === child) active = null;
  if (stopping || timedOut) throw new Error(messages.timeout);
  if (out.overflow || err.overflow) throw new Error(messages.dockerIdentity);
  if (result.error) throw new Error(messages.dockerUnavailable);
  return Object.freeze({ code: result.code, stdout: out.text, stderr: err.text });
}

export function ensureSuccess(result, failureMessage) {
  if (result.code !== 0) throw new Error(failureMessage);
  return result;
}

async function drain(stream, capture, child) {
  let text = '';
  let length = 0;
  let overflow = false;
  for await (const chunk of stream) {
    length += chunk.length;
    if (capture && length <= localImage.maxOutputBytes) text += chunk.toString('utf8');
    else if (capture && !overflow) {
      overflow = true;
      terminate(child, 'SIGTERM');
    }
  }
  return { text, overflow };
}

function terminate(child, signal) {
  try {
    if (process.platform !== 'win32' && Number.isInteger(child.pid)) process.kill(-child.pid, signal);
    else if (!child.killed) child.kill(signal);
  } catch (error) {
    if (error?.code !== 'ESRCH') throw error;
  }
}
