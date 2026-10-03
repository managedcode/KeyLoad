import { constants as fsConstants } from 'node:fs';
import { open } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { createHash } from 'node:crypto';

const MAX_LOG_BYTES = 16 * 1024 * 1024;
const LOG_MARKER = Buffer.from('\n[output truncated at the 16 MiB safety bound]\n');
const PROCESS_TIMEOUT_MS = 15 * 60 * 1000;
const TREE_STOP_GRACE_MS = 5_000;

export async function runBenchmarkProcess({ executable, args, cwd, environment, outputDirectory, signal }) {
  const captures = await openCaptureFiles(outputDirectory);
  if (signal?.aborted) {
    await closeCaptureFiles(captures);
    throw new Error('The benchmark process was cancelled before it started.');
  }
  let child;
  try {
    child = createOwnedChild(executable, args, cwd, environment);
  } catch (error) {
    await closeCaptureFiles(captures);
    throw error;
  }
  const monitor = monitorChild(child, signal, captures);
  try {
    return await settleChild(child, monitor);
  } finally {
    monitor.dispose();
    await closeCaptureFiles(captures);
  }
}

async function openCaptureFiles(directory) {
  const stdout = await openNewLog(path.join(directory, 'benchmark.stdout.log'));
  try {
    const stderr = await openNewLog(path.join(directory, 'benchmark.stderr.log'));
    return { stdout, stderr };
  } catch (error) {
    await stdout.close();
    throw error;
  }
}

async function closeCaptureFiles(captures) {
  await Promise.all([captures.stdout.close(), captures.stderr.close()]);
}

function createOwnedChild(executable, args, cwd, environment) {
  try {
    return spawn(executable, args, { cwd, env: environment, stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true, detached: process.platform !== 'win32' });
  } catch (error) {
    throw new Error('The benchmark process could not be created.', { cause: error });
  }
}

function monitorChild(child, signal, captures) {
  let spawnError;
  let resolveFailure;
  const failure = new Promise((resolve) => { resolveFailure = resolve; });
  let failureSet = false;
  const fail = (reason) => {
    if (failureSet) return;
    failureSet = true;
    resolveFailure(reason);
  };
  const closed = new Promise((resolve) => {
    child.once('error', (error) => { spawnError = error; fail('spawn-error'); });
    child.once('close', (code, childSignal) => resolve({ code, signal: childSignal }));
  });
  const onAbort = () => fail('cancelled');
  signal?.addEventListener('abort', onAbort, { once: true });
  if (signal?.aborted) fail('cancelled');
  const timer = setTimeout(() => fail('timeout'), PROCESS_TIMEOUT_MS);
  const stdoutTask = captureStream(child.stdout, captures.stdout, 'benchmark.stdout.log', fail);
  const stderrTask = captureStream(child.stderr, captures.stderr, 'benchmark.stderr.log', fail);
  const logs = Promise.allSettled([stdoutTask, stderrTask]);
  return { closed, failure, logs, get spawnError() { return spawnError; }, dispose() {
    clearTimeout(timer);
    signal?.removeEventListener('abort', onAbort);
  } };
}

function captureStream(stream, file, name, fail) {
  return pump(stream, file, name, fail).catch((error) => { fail('capture-error'); throw error; });
}

async function settleChild(child, monitor) {
  let outcome = await Promise.race([monitor.closed.then((result) => ({ kind: 'closed', result })),
    monitor.failure.then((reason) => ({ kind: 'failed', reason }))]);
  const terminationFailure = await stopForOutcome(child, monitor, outcome);
  const result = await monitor.closed;
  const logs = await settleLogs(monitor);
  if (terminationFailure) throw new Error('Stopping and joining the owned benchmark process tree failed.', { cause: terminationFailure });
  if (logs.some((log) => log.truncated)) throw new Error('The bounded process logs reached their maximum size.');
  if (outcome.kind === 'failed') throw processFailure(outcome.reason);
  if (monitor.spawnError) throw new Error('The benchmark process failed to start.', { cause: monitor.spawnError });
  if (result.code !== 0) throw new Error(`The BenchmarkDotNet process exited with code ${result.code ?? result.signal}.`);
  return { exitCode: result.code, stdout: logs[0], stderr: logs[1] };
}

async function stopForOutcome(child, monitor, outcome) {
  const orphaned = outcome.kind === 'closed' && child.pid !== undefined && process.platform !== 'win32'
    && processGroupExists(child.pid);
  if (outcome.kind !== 'failed' && !orphaned) return undefined;
  if (orphaned) {
    outcome.kind = 'failed';
    outcome.reason = 'orphaned-descendant';
  }
  try {
    await stopChildTree(child, monitor.closed);
    return undefined;
  } catch (error) {
    return error;
  }
}

async function settleLogs(monitor) {
  const settled = await monitor.logs;
  const failures = settled.filter((item) => item.status === 'rejected').map((item) => item.reason);
  if (failures.length > 0) {
    throw new AggregateError(failures, 'Joining benchmark process output capture failed.');
  }
  return settled.map((item) => item.value);
}

function processFailure(reason) {
  const messages = {
    cancelled: 'The owned benchmark process was cancelled after its tree settled.',
    timeout: 'The owned benchmark process exceeded its 15-minute deadline.',
    'log-limit': 'The owned benchmark process exceeded its bounded log output.',
    'capture-error': 'Capturing bounded benchmark process output failed.',
    'orphaned-descendant': 'The benchmark process left a descendant running after its leader exited.',
    'spawn-error': 'The owned benchmark process could not be started.'
  };
  return new Error(messages[reason] ?? 'The owned benchmark process failed.');
}

async function openNewLog(filePath) {
  return open(filePath, fsConstants.O_CREAT | fsConstants.O_EXCL | fsConstants.O_WRONLY
    | (fsConstants.O_NOFOLLOW ?? 0), 0o600);
}

async function pump(stream, file, label, onOverflow) {
  const hash = createHash('sha256');
  let size = 0;
  let overflow = false;
  for await (const chunk of stream) {
    const bytes = Buffer.from(chunk);
    const contentLimit = MAX_LOG_BYTES - LOG_MARKER.length;
    const remaining = contentLimit - size;
    if (remaining <= 0) {
      if (!overflow) {
        overflow = true;
        onOverflow('log-limit');
      }
      continue;
    }
    const retained = bytes.subarray(0, remaining);
    await file.writeFile(retained);
    hash.update(retained);
    size += retained.length;
    if (retained.length !== bytes.length && !overflow) {
      overflow = true;
      onOverflow('log-limit');
    }
  }
  if (overflow) {
    await file.writeFile(LOG_MARKER);
    hash.update(LOG_MARKER);
    size += LOG_MARKER.length;
  }
  await file.sync();
  return { path: label, bytes: size, sha256: hash.digest('hex'), truncated: overflow };
}

async function stopChildTree(child, closed) {
  const alreadyClosed = child.exitCode !== null || child.signalCode !== null;
  let stopFailure;
  if (!alreadyClosed && child.pid !== undefined && process.platform !== 'win32') {
    try {
      process.kill(-child.pid, 'SIGTERM');
    } catch (error) {
      if (error?.code !== 'ESRCH') stopFailure = error;
    }
  } else if (!alreadyClosed && child.pid !== undefined) {
    try {
      await stopWindowsTree(child.pid);
    } catch (error) {
      stopFailure = error;
    }
  } else if (!alreadyClosed) {
    try {
      child.kill('SIGTERM');
    } catch (error) {
      stopFailure = error;
    }
  }
  if (!alreadyClosed) await Promise.race([closed, wait(TREE_STOP_GRACE_MS)]);
  if (child.pid !== undefined && process.platform !== 'win32' && processGroupExists(child.pid)) {
    try {
      process.kill(-child.pid, 'SIGKILL');
    } catch (error) {
      if (error?.code !== 'ESRCH') stopFailure ??= error;
    }
  } else if (child.pid !== undefined && process.platform === 'win32' && child.exitCode === null) {
    try {
      await stopWindowsTree(child.pid);
    } catch (error) {
      stopFailure ??= error;
    }
  } else if (child.pid === undefined && child.exitCode === null) {
    try {
      child.kill('SIGKILL');
    } catch (error) {
      stopFailure ??= error;
    }
  }
  await closed;
  if (child.pid !== undefined && process.platform !== 'win32') {
    await waitForProcessGroupExit(child.pid);
  }
  if (stopFailure) throw stopFailure;
}

async function waitForProcessGroupExit(pid) {
  const deadline = Date.now() + TREE_STOP_GRACE_MS;
  while (processGroupExists(pid)) {
    if (Date.now() >= deadline) throw new Error('The owned benchmark process group did not exit after termination.');
    await wait(100);
  }
}

function processGroupExists(pid) {
  try {
    process.kill(-pid, 0);
    return true;
  } catch (error) {
    if (error?.code === 'ESRCH') return false;
    if (error?.code === 'EPERM') return true;
    throw error;
  }
}

async function stopWindowsTree(pid) {
  const helper = spawn('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
  let spawnError;
  const closed = new Promise((resolve) => {
    helper.once('error', (error) => { spawnError = error; });
    helper.once('close', (code) => resolve(code));
  });
  const result = await Promise.race([closed.then((code) => ({ code })), wait(10_000).then(() => ({ timeout: true }))]);
  if (result.timeout) {
    helper.kill('SIGKILL');
    await closed;
    throw new Error('The owned Windows process-tree stop helper exceeded its deadline.');
  }
  if (spawnError) throw spawnError;
  if (result.code !== 0) throw new Error('Could not stop the owned benchmark process tree.');
}

function wait(durationMs) {
  return new Promise((resolve) => setTimeout(resolve, durationMs));
}
