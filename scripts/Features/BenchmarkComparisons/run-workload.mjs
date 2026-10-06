import { spawn } from 'node:child_process';
import { constants } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { openLoopWorkloadArguments, selectOpenLoopWorkload } from './open-loop-workload-selection.mjs';

const intervalMilliseconds = 30_000;
const maximumProgressBytes = 512;
const maximumSnapshotBytes = maximumProgressBytes + 1;
const maximumInteger = 2_147_483_647;
const marker = /^KeyLoadBenchmarkProgress phase=(oracle|initialize|warmup|prepare|measure|validate|complete) repetition=(\d{1,10}) completed=(\d{1,10}) total=(\d{1,10}) failed=(\d{1,10}) elapsedSeconds=(\d+(?:\.\d+)?)$/;
const missingFile = 'ENOENT';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const controlProfile = 'intensive-1k-c16';
const scaleProfiles = new Set(['scaled-100k-c16', 'scaled-1m-c16']);
const vectorProfilePattern = /^vector-(?:100k|1m)-(?:exact|hnsw|ivfflat|native)-(?:plain|filtered|mixed)-c16$/u;

export function validProgress(line) {
  if (line.length > maximumProgressBytes) return false;
  const match = marker.exec(line);
  if (!match) return false;
  const [repetition, completed, total, failed, elapsed] = match.slice(2).map(Number);
  return [repetition, completed, total, failed].every(value => Number.isInteger(value) && value <= maximumInteger)
    && Number.isFinite(elapsed) && failed <= completed && completed <= total;
}

export function selectedScaleProfile(environment, vectorProfile) {
  const scaleProfile = environment.KEYLOAD_SCALE_PROFILE;
  if (vectorProfile !== undefined) {
    if ((scaleProfile ?? '') !== '') throw new Error('The native comparison profile identity is invalid.');
    return undefined;
  }
  if (scaleProfile !== undefined && scaleProfile !== '') {
    if (!scaleProfiles.has(scaleProfile) || environment.Benchmarks__EvidenceProfile !== scaleProfile) {
      throw new Error('The native comparison profile identity is invalid.');
    }
    return scaleProfile;
  }
  if (environment.Benchmarks__EvidenceProfile !== controlProfile) {
    throw new Error('The native comparison profile identity is invalid.');
  }
  return undefined;
}

export function selectedVectorProfile(environment) {
  const vectorProfile = environment.KEYLOAD_VECTOR_PROFILE;
  if (vectorProfile !== undefined && vectorProfile !== '') {
    if (!vectorProfilePattern.test(vectorProfile) || environment.Benchmarks__VectorProfile !== vectorProfile
      || environment.Benchmarks__EvidenceProfile !== vectorProfile || (environment.KEYLOAD_SCALE_PROFILE ?? '') !== '') {
      throw new Error('The native comparison profile identity is invalid.');
    }
    return vectorProfile;
  }
  if ((environment.Benchmarks__VectorProfile ?? '') !== '') {
    throw new Error('The native comparison profile identity is invalid.');
  }
  return undefined;
}

export function workloadArguments(scaleProfile, vectorProfile, openLoopCell) {
  if (scaleProfile !== undefined && vectorProfile !== undefined) throw new Error('The native comparison profile identity is invalid.');
  const scaled = scaleProfile !== undefined;
  const vector = vectorProfile !== undefined;
  const openLoop = openLoopCell === undefined ? undefined : openLoopWorkloadArguments(openLoopCell);
  if (openLoop !== undefined && (!scaled || vector || openLoopCell.profile !== scaleProfile)) {
    throw new Error('The native comparison profile identity is invalid.');
  }
  const filter = openLoop?.filter ?? '/*/*/IsolatedNativeComparisonTests/*';
  const arguments_ = [
    'run', '--project', 'src/KeyLoad.AppHost', '--no-build', '--no-restore', '--configuration', 'Release', '--',
    '--KeyLoadTests:Suite=comparison', '--KeyLoadTests:Filter=' + filter,
    `--KeyLoadTests:TimeoutMinutes=${vector || scaled ? 140 : 60}`
  ];
  if (scaled) arguments_.push(`--KeyLoadTests:ScaleProfile=${scaleProfile}`);
  if (vector) arguments_.push(`--KeyLoadTests:VectorProfile=${vectorProfile}`);
  if (openLoop !== undefined) arguments_.push(openLoop.rateArgument);
  return arguments_;
}

export async function readProgress(file) {
  let handle;
  try {
    const metadata = await lstat(file);
    if (!metadata.isFile() || metadata.size > maximumSnapshotBytes) return null;
    handle = await open(file, constants.O_RDONLY | constants.O_NOFOLLOW);
    const opened = await handle.stat();
    if (!opened.isFile() || opened.size > maximumSnapshotBytes) return null;
    const buffer = Buffer.alloc(maximumSnapshotBytes + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    if (bytesRead > maximumSnapshotBytes) return null;
    const line = buffer.subarray(0, bytesRead).toString('utf8').trimEnd();
    return validProgress(line) ? line : null;
  } catch (error) {
    if (error.code === missingFile) return null;
    return null;
  } finally {
    await handle?.close().catch(() => {});
  }
}

function signalChild(child, signal) {
  if (!Number.isInteger(child.pid)) return;
  try {
    if (process.platform === 'win32') child.kill(signal);
    else process.kill(-child.pid, signal);
  } catch (error) {
    if (error.code !== 'ESRCH') throw error;
  }
}

function ownSignals(child) {
  let requested;
  let force;
  let terminate;
  const cancel = signal => {
    if (requested) return;
    requested = signal;
    signalChild(child, signal);
    terminate = setTimeout(() => signalChild(child, 'SIGTERM'), 35_000);
    force = setTimeout(() => signalChild(child, 'SIGKILL'), 45_000);
  };
  const interrupt = () => cancel('SIGINT');
  const terminateHandler = () => cancel('SIGTERM');
  process.on('SIGINT', interrupt);
  process.on('SIGTERM', terminateHandler);
  return {
    result: () => requested === 'SIGINT' ? 130 : requested === 'SIGTERM' ? 143 : null,
    close: () => {
      clearTimeout(force);
      clearTimeout(terminate);
      process.off('SIGINT', interrupt);
      process.off('SIGTERM', terminateHandler);
    }
  };
}

async function observeProgress(file, childExit, period) {
  const lifetime = new AbortController();
  const started = Date.now();
  const completion = childExit.then(() => lifetime.abort());
  while (!lifetime.signal.aborted) {
    try { await delay(period, undefined, { signal: lifetime.signal }); }
    catch (error) { if (error.name !== 'AbortError') throw error; }
    const line = await readProgress(file);
    if (line) await writeProgress(`${line}\n`);
    else if (!lifetime.signal.aborted) await writeProgress(`Waiting for native benchmark progress; AppHost elapsed ${Math.floor((Date.now() - started) / 1000)}s.\n`);
  }
  await completion;
}

async function writeProgress(line) {
  await new Promise(resolve => {
    try { process.stdout.write(line, () => resolve()); }
    catch { resolve(); }
  });
}

// Tests exercise this lifecycle with genuine short-lived child processes.
// The production entry below always runs the repository's closed Aspire command.
export async function runProgressProcess(command, arguments_, workingDirectory, progressFile, period = intervalMilliseconds) {
  if (!Number.isInteger(period) || period < 1 || period > intervalMilliseconds) throw new Error('Invalid progress interval.');
  const environment = { ...process.env };
  delete environment.Benchmarks__VectorProfile;
  const child = spawn(command, arguments_, { env: environment, cwd: workingDirectory, stdio: 'inherit', detached: process.platform !== 'win32' });
  const signals = ownSignals(child);
  const outputError = () => {};
  process.stdout.on('error', outputError);
  const completion = new Promise(resolve => {
    child.once('error', () => resolve(1));
    child.once('exit', (code, signal) => resolve(code ?? (signal === 'SIGINT' ? 130 : signal === 'SIGTERM' ? 143 : 1)));
  });
  try {
    await observeProgress(progressFile, completion, period);
    return signals.result() ?? await completion;
  } finally {
    signals.close();
    process.stdout.off('error', outputError);
  }
}

export async function runWorkload() {
  const cell = process.env.KEYLOAD_COMPARISON_CELL_ID;
  if (typeof cell !== 'string' || cell.length > 256 || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(cell)) {
    throw new Error('The native comparison cell identity is invalid.');
  }
  const vectorProfile = selectedVectorProfile(process.env);
  const scaleProfile = selectedScaleProfile(process.env, vectorProfile);
  const openLoopCell = selectOpenLoopWorkload(process.env, scaleProfile, vectorProfile);
  const progress = path.join(root, 'artifacts/comparisons/isolated/failures', cell, 'progress.log');
  const arguments_ = workloadArguments(scaleProfile, vectorProfile, openLoopCell);
  return await runProgressProcess('dotnet', arguments_, root, progress);
}

const evaluated = process.execArgv.some(argument => argument === '--eval' || argument === '-e');
if (!evaluated && process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runWorkload().then(code => { process.exitCode = code; }).catch(() => {
    process.stderr.write('Native benchmark entry failed.\n');
    process.exitCode = 1;
  });
}
