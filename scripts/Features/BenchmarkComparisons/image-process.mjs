import { spawn } from 'node:child_process';
import { message, outputFormat, processLimit } from './image-contracts.mjs';

const streamName = Object.freeze({ stdout: 'stdout', stderr: 'stderr' });
const signalName = Object.freeze({ terminate: 'SIGTERM', force: 'SIGKILL' });
const emptyBuffer = Buffer.alloc(0);
const spawnError = 'error';
const closeEvent = 'close';

export function runBounded(command, argumentsList, options = {}) {
  const child = spawn(command, argumentsList, {
    cwd: options.cwd,
    env: options.environment ?? process.env,
    shell: false,
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const state = {
    output: { [streamName.stdout]: emptyBuffer, [streamName.stderr]: emptyBuffer },
    outputBytes: 0,
    timedOut: false,
    exceededOutput: false,
    outputTruncated: false,
    spawnFailed: false,
    forceKillScheduled: false,
    forceKillTimer: null,
    timeout: null,
  };
  return new Promise(resolve => monitorChild(child, state, options, resolve));
}

function monitorChild(child, state, options, resolve) {
  let settled = false;
  const finish = (code, signal) => {
    if (settled) return;
    settled = true;
    if (state.timeout) clearTimeout(state.timeout);
    if (state.forceKillTimer) clearTimeout(state.forceKillTimer);
    resolve(makeResult(state, code, signal));
  };
  const capture = (name, chunk) => captureChunk(child, state, name, chunk, options);
  state.timeout = setTimeout(() => {
    state.timedOut = true;
    scheduleOwnedKill(child, state);
  }, options.timeoutMs ?? processLimit.commandTimeoutMs);
  child.stdout.on('data', chunk => capture(streamName.stdout, chunk));
  child.stderr.on('data', chunk => capture(streamName.stderr, chunk));
  child.on(spawnError, () => {
    state.spawnFailed = true;
    finish(null, null);
  });
  child.on(closeEvent, (code, signal) => finish(code, signal));
}

function captureChunk(child, state, name, chunk, options) {
  const maximumOutputBytes = options.maximumOutputBytes ?? processLimit.maxOutputBytes;
  const availableBytes = Math.max(0, maximumOutputBytes - state.outputBytes);
  const capturedBytes = Math.min(chunk.length, availableBytes);
  if (capturedBytes > 0) {
    state.output[name] = Buffer.concat([state.output[name], chunk.subarray(0, capturedBytes)]);
    state.outputBytes += capturedBytes;
  }
  if (capturedBytes < chunk.length) {
    if (options.truncateOutput === true) state.outputTruncated = true;
    else {
      state.exceededOutput = true;
      scheduleOwnedKill(child, state);
    }
  }
}

function scheduleOwnedKill(child, state) {
  if (state.forceKillScheduled) return;
  state.forceKillScheduled = true;
  child.kill(signalName.terminate);
  state.forceKillTimer = setTimeout(() => child.kill(signalName.force), processLimit.killGraceMs);
}

function makeResult(state, code, signal) {
  const stdout = state.output[streamName.stdout].toString(outputFormat.utf8);
  const stderr = state.output[streamName.stderr].toString(outputFormat.utf8);
  return Object.freeze({
    code,
    signal,
    stdout,
    stderr,
    timedOut: state.timedOut,
    outputLimitExceeded: state.exceededOutput,
    outputTruncated: state.outputTruncated,
    spawnFailed: state.spawnFailed,
    success: code === 0 && signal === null && !state.timedOut && !state.exceededOutput && !state.spawnFailed,
  });
}

export async function requireSuccessful(command, argumentsList, options = {}) {
  const result = await runBounded(command, argumentsList, options);
  if (typeof options.recordResult === 'function') await options.recordResult(result);
  if (result.timedOut) throw new Error(message.commandTimeout);
  if (result.outputLimitExceeded) throw new Error(message.commandOutputLimit);
  if (!result.success) throw new Error(message.commandFailed);
  return result.stdout;
}
