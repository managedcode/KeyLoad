import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import { writeSync } from 'node:fs';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { spawn } from 'node:child_process';

const maximumArguments = 8;
const decimalPattern = /^[1-9][0-9]*$/;
const imageIdPattern = /^sha256:[0-9a-f]{64}$/;
const safeFailure = 'Original-node coverage image preparation failed.\n';
const safeChildTimeout = 'The original native child exceeded its operation bound.';
const safeSettlementTimeout = 'The original native child or readers did not settle within the captured deadline.';
const safeOutputOverflow = 'Original native child output exceeded its bound.';

function positive(value) {
  if (typeof value !== 'string' || !decimalPattern.test(value) || !Number.isSafeInteger(Number(value))) {
    throw new Error('invalid');
  }
  return Number(value);
}

async function collect(stream, bound, state) {
  const chunks = [];
  let length = 0;
  for await (const chunk of stream) {
    length += chunk.length;
    state.totalBytes += chunk.length;
    if (state.totalBytes > bound) {
      if (!state.overflow) {
        state.overflow = true;
        state.onOverflow();
      }
      continue;
    }
    chunks.push(chunk);
  }
  return Buffer.concat(chunks, length <= bound ? length : 0);
}

function observe(promise, outcomes, index) {
  return promise.then(value => {
    const outcome = { value };
    outcomes[index] = outcome;
    return outcome;
  }, failure => {
    const outcome = { failure };
    outcomes[index] = outcome;
    return outcome;
  });
}

async function waitBefore(deadline, tasks) {
  const remaining = Math.max(0, deadline - performance.now());
  let timer;
  const timeout = new Promise(resolve => { timer = setTimeout(() => resolve(false), remaining); });
  try {
    return await Promise.race([Promise.all(tasks).then(() => true), timeout]);
  } finally {
    clearTimeout(timer);
  }
}

async function waitForOutcome(deadline, tasks, overflow) {
  const remaining = Math.max(0, deadline - performance.now());
  let timer;
  const timeout = new Promise(resolve => { timer = setTimeout(() => resolve('timeout'), remaining); });
  try {
    return await Promise.race([
      Promise.all(tasks).then(() => 'settled'),
      overflow.then(() => 'overflow'),
      timeout,
    ]);
  } finally {
    clearTimeout(timer);
  }
}

function closeTask(child, errors) {
  return new Promise(resolve => {
    child.once('error', failure => errors.push(failure));
    child.once('close', (code, signal) => resolve({ code, signal }));
  });
}

function requestStop(child, signal, failures) {
  if (!Number.isInteger(child.pid) || child.pid <= 0) return;
  try {
    process.kill(-child.pid, signal);
  } catch (failure) {
    if (failure.code !== 'ESRCH') failures.push(failure);
  }
}

function settleFailure(primary, failures, originalTasksUnsettled = false) {
  const ordered = [primary, ...failures];
  const failure = ordered.length === 1
    ? primary : new AggregateError(ordered, 'Original native child settlement failed.');
  failure.originalTasksUnsettled = originalTasksUnsettled;
  throw failure;
}

async function settle(child, stdout, stderr, timeoutMs, settlementMs) {
  const childErrors = [];
  const outcomes = [];
  const failures = [];
  const stopFailures = [];
  let signalOverflow;
  const overflow = new Promise(resolve => { signalOverflow = resolve; });
  const state = {
    overflow: false,
    totalBytes: 0,
    cleanupDeadline: null,
    onOverflow: () => {
      state.cleanupDeadline = performance.now() + settlementMs;
      requestStop(child, 'SIGTERM', stopFailures);
      signalOverflow();
    },
  };
  const tasks = [
    observe(closeTask(child, childErrors), outcomes, 0),
    observe(collect(child.stdout, stdout.bound, state), outcomes, 1),
    observe(collect(child.stderr, stderr.bound, state), outcomes, 2),
  ];
  const operationDeadline = performance.now() + timeoutMs;
  const outcome = await waitForOutcome(operationDeadline, tasks, overflow);
  if (outcome !== 'settled') {
    const outputOverflowed = state.overflow;
    const primary = childErrors[0] ?? new Error(outputOverflowed ? safeOutputOverflow : safeChildTimeout);
    failures.push(...childErrors.slice(1));
    failures.push(...stopFailures);
    const cleanupDeadline = state.cleanupDeadline ?? operationDeadline + settlementMs;
    if (!outputOverflowed) requestStop(child, 'SIGTERM', failures);
    const forceKillDeadline = cleanupDeadline - settlementMs / 2;
    let settled = await waitBefore(forceKillDeadline, tasks);
    if (!settled) {
      requestStop(child, 'SIGKILL', failures);
      settled = await waitBefore(cleanupDeadline, tasks);
    }
    for (const outcome of outcomes) {
      if (outcome && 'failure' in outcome && !failures.includes(outcome.failure)) {
        failures.push(outcome.failure);
      }
    }
    if (state.overflow && outcome === 'timeout') failures.push(new Error(safeOutputOverflow));
    if (!settled) failures.push(new Error(safeSettlementTimeout));
    settleFailure(primary, failures, !settled);
  }
  const [closeResult, stdoutResult, stderrResult] = await Promise.all(tasks);
  failures.push(...childErrors);
  failures.push(...stopFailures);
  if ('failure' in closeResult) failures.push(closeResult.failure);
  if ('failure' in stdoutResult) failures.push(stdoutResult.failure);
  if ('failure' in stderrResult) failures.push(stderrResult.failure);
  const close = closeResult.value;
  if (state.overflow) failures.push(new Error(safeOutputOverflow));
  if (state.overflow || close.code !== 0 || close.signal !== null) {
    failures.push(new Error('The original native child returned an invalid terminal result.'));
  }
  if (failures.length > 0) settleFailure(failures[0], failures.slice(1));
  return { outBytes: stdoutResult.value, errorBytes: stderrResult.value };
}

async function run(executable, args, cwd, maximumBytes, timeoutMs, settlementMs) {
  const child = spawn(executable, args, {
    cwd, shell: false, windowsHide: true, detached: process.platform !== 'win32',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  return settle(child, { bound: maximumBytes }, { bound: maximumBytes }, timeoutMs, settlementMs);
}

async function writeCreateOnly(destination, bytes) {
  await fs.mkdir(path.dirname(destination), { recursive: true });
  const temporary = `${destination}.${crypto.randomUUID()}.pending`;
  let handle;
  let ownsTemporary = false;
  const failures = [];
  try {
    handle = await fs.open(temporary, 'wx', 0o600);
    ownsTemporary = true;
    await handle.writeFile(bytes);
    await handle.sync();
  } catch (failure) {
    failures.push(failure);
  }
  if (handle) {
    try {
      await handle.close();
    } catch (failure) {
      failures.push(failure);
    }
  }
  try {
    if (failures.length === 0) {
      await fs.link(temporary, destination);
    }
  } catch (failure) {
    failures.push(failure);
  }
  if (ownsTemporary) {
    try {
      await fs.unlink(temporary);
    } catch (failure) {
      failures.push(failure);
    }
  }
  if (failures.length === 1) {
    throw failures[0];
  }
  if (failures.length > 1) {
    throw new AggregateError(failures, 'Create-only image receipt publication failed.');
  }
}

function validateMaterializerReceipt(bytes) {
  const receipt = JSON.parse(bytes.toString('utf8'));
  const keys = Object.keys(receipt).sort();
  if (keys.join(',') !== 'contextDirectory,fileCount,manifestPath,manifestSha256,totalBytes' ||
      typeof receipt.contextDirectory !== 'string' || typeof receipt.manifestPath !== 'string' ||
      !/^[0-9a-f]{64}$/.test(receipt.manifestSha256) || !Number.isSafeInteger(receipt.fileCount) ||
      !Number.isSafeInteger(receipt.totalBytes) || receipt.fileCount < 1 || receipt.totalBytes < 1) {
    throw new Error('receipt');
  }
  return receipt;
}

async function readRegularBounded(filePath, maximumBytes) {
  const before = await fs.lstat(filePath);
  if (!before.isFile() || before.isSymbolicLink() || before.size <= 0 || before.size > maximumBytes) {
    throw new Error('input');
  }
  const handle = await fs.open(filePath, 'r');
  let bytes;
  let primary;
  try {
    const opened = await handle.stat();
    if (!opened.isFile() || opened.dev !== before.dev || opened.ino !== before.ino ||
        opened.size !== before.size || opened.mtimeMs !== before.mtimeMs ||
        opened.size <= 0 || opened.size > maximumBytes) {
      throw new Error('changed');
    }
    bytes = Buffer.alloc(opened.size);
    let offset = 0;
    while (offset < bytes.length) {
      const read = await handle.read(bytes, offset, bytes.length - offset, offset);
      if (read.bytesRead === 0) throw new Error('changed');
      offset += read.bytesRead;
    }
    const extra = Buffer.alloc(1);
    if ((await handle.read(extra, 0, extra.length, bytes.length)).bytesRead !== 0) {
      throw new Error('changed');
    }
    const after = await handle.stat();
    if (after.size !== opened.size || after.mtimeMs !== opened.mtimeMs) throw new Error('changed');
  } catch (failure) {
    primary = failure;
  }
  try {
    await handle.close();
  } catch (failure) {
    if (primary) throw new AggregateError([primary, failure], 'Bounded file read and handle cleanup failed.');
    throw failure;
  }
  if (primary) throw primary;
  return bytes;
}

function sha256(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

async function verifyBaseReceipt(root, maximumBytes, sourceRevision) {
  const receiptPath = path.join(root, 'functional-coverage.base-image.v1.json');
  const bytes = await readRegularBounded(receiptPath, maximumBytes);
  const receipt = JSON.parse(bytes.toString('utf8'));
  const keys = Object.keys(receipt).sort();
  if (keys.join(',') !== 'imageReference,schemaVersion,sourcePath,sourceRevision,sourceSha256' ||
      receipt.schemaVersion !== 1 || receipt.sourceRevision !== sourceRevision || receipt.sourcePath !== 'Dockerfile' ||
      !/^[0-9a-f]{64}$/.test(receipt.sourceSha256) || typeof receipt.imageReference !== 'string' ||
      !receipt.imageReference.startsWith('mcr.microsoft.com/dotnet/aspnet:') ||
      !/@sha256:[0-9a-f]{64}$/.test(receipt.imageReference)) {
    throw new Error('base-receipt');
  }
  const dockerfilePath = path.resolve(process.cwd(), receipt.sourcePath);
  const dockerfile = await readRegularBounded(dockerfilePath, maximumBytes);
  if (sha256(dockerfile) !== receipt.sourceSha256) {
    throw new Error('base-changed');
  }
  return { path: dockerfilePath, bytes, sha256: receipt.sourceSha256, imageReference: receipt.imageReference };
}

async function verifyBaseUnchanged(base, maximumBytes) {
  const bytes = await readRegularBounded(base.path, maximumBytes);
  if (sha256(bytes) !== base.sha256) {
    throw new Error('base-changed');
  }
}

async function main(args) {
  if (args.length !== maximumArguments || args[0] !== 'prepare') {
    throw new Error('arguments');
  }
  const [invocationPath, outputRoot, imageReference, materializerPath, maximumBytesText,
    operationSecondsText, settlementSecondsText] = args.slice(1);
  const maximumBytes = positive(maximumBytesText);
  const operationSeconds = positive(operationSecondsText);
  const settlementSeconds = positive(settlementSecondsText);
  const root = path.resolve(outputRoot);
  const invocationBytes = await readRegularBounded(invocationPath, maximumBytes);
  const invocation = JSON.parse(invocationBytes.toString('utf8'));
  const context = path.resolve(invocation.contextDirectory);
  const runPath = path.join(root, 'functional-coverage.rf3-run.v1.json');
  const runManifest = JSON.parse((await readRegularBounded(runPath, maximumBytes)).toString('utf8'));
  const base = await verifyBaseReceipt(root, maximumBytes, runManifest.sourceRevision);
  const expectedImageReference = `keyload/functional-coverage:${runManifest.runId.replaceAll('-', '')}`;
  if (!context.startsWith(`${root}${path.sep}`) || imageReference !== expectedImageReference ||
      invocation.baseImage.reference !== base.imageReference) {
    throw new Error('path');
  }
  const materializerArgs = [materializerPath, `--invocation=${invocationPath}`,
    `--maximum-descriptor-bytes=${maximumBytes}`];
  await verifyBaseUnchanged(base, maximumBytes);
  const materialized = await run(process.execPath, materializerArgs, path.dirname(materializerPath), maximumBytes,
    operationSeconds * 1000, settlementSeconds * 1000);
  await verifyBaseUnchanged(base, maximumBytes);
  const materializerReceipt = validateMaterializerReceipt(materialized.outBytes);
  if (path.resolve(materializerReceipt.contextDirectory) !== context) {
    throw new Error('context');
  }
  await writeCreateOnly(path.join(root, 'functional-coverage.materializer-result.json'), materialized.outBytes);
  const build = await run('docker', ['build', '--tag', imageReference, context], root, maximumBytes,
    operationSeconds * 1000, settlementSeconds * 1000);
  void build;
  const inspected = await run('docker', ['image', 'inspect', imageReference, '--format', '{{json .Id}}'], root,
    maximumBytes, operationSeconds * 1000, settlementSeconds * 1000);
  const imageId = JSON.parse(inspected.outBytes.toString('utf8'));
  if (typeof imageId !== 'string' || !imageIdPattern.test(imageId)) {
    throw new Error('image');
  }
  await writeCreateOnly(path.join(root, 'functional-coverage.image-inspect.json'), inspected.outBytes);
}

try {
  await main(process.argv.slice(2));
} catch (failure) {
  if (failure.originalTasksUnsettled === true) {
    try {
      writeSync(process.stderr.fd, safeFailure);
    } finally {
      process.exit(1);
    }
  }
  process.stderr.write(safeFailure);
  process.exitCode = 1;
}
