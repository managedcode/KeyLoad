import { spawn } from 'node:child_process';
import { localImage, messages } from './local-image-contracts.mjs';

let stopping = false;
let active = null;

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.once(signal, () => {
    stopping = true;
    if (active) {
      try { active.stop(signal); } catch { /* Preserve signal settlement. */ }
    }
  });
}

export async function runDocker(root, args, timeoutMs, capture = true, retainTail = false) {
  if (stopping) throw new Error(messages.timeout);
  const child = spawn(localImage.docker, args, {
    cwd: root,
    detached: process.platform !== 'win32',
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });
  const failures = [];
  let timedOut = false;
  let overflow = false;
  let killGrace;
  let stoppingChild = false;
  const stopChild = (signal = 'SIGTERM') => {
    if (stoppingChild) return;
    stoppingChild = true;
    try { terminate(child, signal); }
    catch (error) { failures.push(error); }
    killGrace = setTimeout(() => {
      try { terminate(child, 'SIGKILL'); }
      catch (error) { failures.push(error); }
    }, localImage.killGraceMs);
    killGrace.unref();
  };
  const ownership = Object.freeze({ child, stop: stopChild });
  active = ownership;
  const stdoutTail = createTail(retainTail);
  const stderrTail = createTail(retainTail);
  const stdout = drain(child.stdout, capture, () => {
    overflow = true;
    stopChild();
  }, stopChild, stdoutTail);
  const stderr = drain(child.stderr, capture, () => {
    overflow = true;
    stopChild();
  }, stopChild, stderrTail);
  let spawnError;
  child.once('error', error => {
    spawnError = error;
    stopChild();
  });
  const exit = new Promise(resolve => child.once('close', (code, signal) => resolve({ code, signal })));
  const deadline = setTimeout(() => {
    timedOut = true;
    stopChild();
  }, timeoutMs);
  let settled;
  try {
    settled = await Promise.allSettled([exit, stdout, stderr]);
  } finally {
    clearTimeout(deadline);
    if (killGrace) clearTimeout(killGrace);
    if (active === ownership) active = null;
  }
  const output = settled[1].status === 'fulfilled' ? settled[1].value : null;
  const errorOutput = settled[2].status === 'fulfilled' ? settled[2].value : null;
  if (settled[0].status === 'rejected') failures.push(settled[0].reason);
  if (settled[1].status === 'rejected') failures.push(settled[1].reason);
  if (settled[2].status === 'rejected') failures.push(settled[2].reason);
  if (spawnError) failures.push(spawnError);
  const tails = Object.freeze({ stdoutTail: readTail(stdoutTail), stderrTail: readTail(stderrTail) });
  if (stopping || timedOut || overflow || failures.length !== 0) {
    const message = overflow ? messages.dockerIdentity : timedOut || stopping ? messages.timeout : messages.dockerUnavailable;
    throw new LocalImageProcessFailure(message, failures, tails);
  }
  return Object.freeze({ code: settled[0].value.code, stdout: output.text, stderr: errorOutput.text, ...tails });
}

export function ensureSuccess(result, failureMessage) {
  if (result.code !== 0) throw new Error(failureMessage);
  return result;
}

export function getBuildOutput(error) {
  return error instanceof LocalImageProcessFailure ? error.buildOutput : null;
}

export class LocalImageProcessFailure extends Error {
  constructor(message, failures, tails) {
    super(message);
    this.name = 'LocalImageProcessFailure';
    this.failures = Object.freeze([...failures]);
    this.buildOutput = Object.freeze({ stdoutTail: tails.stdoutTail, stderrTail: tails.stderrTail });
  }
}

async function drain(stream, capture, onOverflow, onFailure, tail) {
  let text = '';
  let length = 0;
  let overflow = false;
  try {
    for await (const chunk of stream) {
      length += chunk.length;
      if (capture && length <= localImage.maxOutputBytes) text += chunk.toString('utf8');
      else if (capture && !overflow) {
        overflow = true;
        onOverflow();
      }
      if (tail) appendTail(tail, chunk);
    }
  } catch (error) {
    onFailure();
    throw error;
  }
  return Object.freeze({ text, overflow });
}

function createTail(enabled) {
  return enabled ? { bytes: Buffer.alloc(localImage.maxOutputBytes), length: 0, offset: 0 } : null;
}

function appendTail(tail, chunk) {
  const buffer = tail.bytes;
  if (chunk.length >= buffer.length) {
    chunk.copy(buffer, 0, chunk.length - buffer.length);
    tail.length = buffer.length;
    tail.offset = 0;
    return;
  }
  const available = buffer.length - tail.length;
  if (chunk.length <= available) {
    chunk.copy(buffer, (tail.offset + tail.length) % buffer.length);
    tail.length += chunk.length;
    return;
  }
  const discard = chunk.length - available;
  tail.offset = (tail.offset + discard) % buffer.length;
  tail.length -= discard;
  const target = (tail.offset + tail.length) % buffer.length;
  const first = Math.min(chunk.length, buffer.length - target);
  chunk.copy(buffer, target, 0, first);
  if (first < chunk.length) chunk.copy(buffer, 0, first);
  tail.length = Math.min(buffer.length, tail.length + chunk.length);
}

function readTail(tail) {
  if (!tail) return Buffer.alloc(0);
  const retained = tail.length < tail.bytes.length
    ? tail.bytes.subarray(0, tail.length)
    : Buffer.concat([tail.bytes.subarray(tail.offset), tail.bytes.subarray(0, tail.offset)]);
  return Buffer.from(retained);
}

function terminate(child, signal) {
  try {
    if (process.platform !== 'win32' && Number.isInteger(child.pid)) process.kill(-child.pid, signal);
    else if (!child.killed) child.kill(signal);
  } catch (error) {
    if (error?.code !== 'ESRCH') throw error;
  }
}
