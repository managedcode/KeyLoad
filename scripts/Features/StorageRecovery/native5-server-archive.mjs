import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createWriteStream } from 'node:fs';
import { constants } from 'node:fs';
import { lstat, open, rm } from 'node:fs/promises';
import path from 'node:path';

const maximumArchiveBytes = 4 * 1024 * 1024 * 1024;
const maximumFiles = 100000;
const maximumDepth = 64;
const maximumPathBytes = 4096;
const maximumTreeBytes = 1024 * 1024;
const maximumStderrBytes = 64 * 1024;
const chunkBytes = 64 * 1024;
const blockBytes = 512;
const archiveDeadlineMs = 60000;
const settlementMs = 10000;
const killGraceMs = 1000;
const invalidSource = 'The immutable native5 source export is invalid.';
const native5Revision = '7784b6b46b98ce994dd98070dc1f58fe4e506b91';

export async function runPriorCommand(command, args, cwd, maximumOutputBytes = maximumTreeBytes) {
  const child = spawn(command, args, { cwd, shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let stdoutBytes = 0;
  let stderrBytes = 0;
  let failed = false;
  const stdout = [];
  const stderr = [];
  let requestStop = () => {};
  const childTask = waitForChild(child, archiveDeadlineMs, () => {}, stopRequest => { requestStop = stopRequest; });
  child.stdout.on('data', chunk => {
    stdoutBytes += chunk.length;
    if (stdoutBytes > maximumOutputBytes) { failed = true; requestStop(); }
    else stdout.push(Buffer.from(chunk));
  });
  child.stderr.on('data', chunk => {
    stderrBytes += chunk.length;
    if (stderrBytes > maximumStderrBytes) { failed = true; requestStop(); }
    else stderr.push(Buffer.from(chunk));
  });
  const outcome = await childTask;
  if (!outcome.settled) throw unsettledError();
  return Object.freeze({
    success: !failed && !outcome.timedOut && outcome.code === 0 && outcome.signal === null,
    code: outcome.code,
    timedOut: outcome.timedOut,
    outputLimitExceeded: failed,
    stdout: Buffer.concat(stdout, Math.min(stdoutBytes, maximumOutputBytes)),
    stderr: Buffer.concat(stderr, Math.min(stderrBytes, maximumStderrBytes)),
  });
}

export async function createAndInspectGitArchive(workspace, target, revision, gitEntries) {
  const initial = await open(target, 'wx', 0o600);
  await initial.close();
  const output = createWriteStream(target, { flags: 'r+', mode: 0o600 });
  const child = spawn('git', ['-c', 'tar.umask=0022', 'archive', '--format=tar', revision], {
    cwd: workspace, shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
  });
  const inspection = new ArchiveInspection(gitEntries, revision);
  let failure;
  let streamEnded = false;
  let requestStop = () => {};
  let settlementStartedAt;
  const childTask = waitForChild(child, archiveDeadlineMs, () => { failure ??= new Error(invalidSource); }, stopRequest => { requestStop = stopRequest; });
  const outputClosed = new Promise(resolve => output.once('close', () => resolve(true)));
  child.on('error', error => { failure ??= error; });
  output.on('error', error => { failure ??= error; requestStop(); });
  child.stderr.on('data', chunk => {
    inspection.stderrBytes += chunk.length;
    if (inspection.stderrBytes > maximumStderrBytes) { failure ??= new Error(invalidSource); requestStop(); }
  });
  child.stdout.on('data', chunk => {
    try {
      inspection.observe(chunk);
      if (!output.write(chunk)) {
        child.stdout.pause();
        output.once('drain', () => child.stdout.resume());
      }
    } catch (error) { failure ??= error; requestStop(); }
  });
  child.stdout.on('end', () => { streamEnded = true; output.end(); });
  const processOutcome = await childTask;
  settlementStartedAt = processOutcome.stopStartedAt ?? Date.now();
  if (!processOutcome.settled) throw unsettledError(failure);
  if (!streamEnded) output.end();
  let didClose = await withSettlement(outputClosed, settlementStartedAt);
  if (!didClose) {
    output.destroy();
    if (!await withSettlement(outputClosed, settlementStartedAt)) throw unsettledError(failure);
    failure ??= new Error(invalidSource);
  }
  if (failure || !processOutcome.success || !inspection.complete()) {
    const primary = failure ?? new Error(invalidSource);
    try { await rm(target, { force: false }); }
    catch (cleanup) { throw new AggregateError([primary, cleanup], 'Archive validation and evidence cleanup failed.'); }
    throw primary;
  }
  const handle = await open(target, 'r+');
  try { await handle.sync(); } finally { await handle.close(); }
  return inspection.result();
}

export async function extractVerifiedArchive(archivePath, destination, workspace) {
  const result = await runPriorCommand('tar', ['--same-permissions', '-xf', archivePath, '-C', destination], workspace, maximumStderrBytes);
  if (!result.success) throw new Error(invalidSource);
}

export async function verifyRetainedArchive(directory, name, rows, expectedBytes, expectedSha256, revision = native5Revision) {
  const target = path.join(directory, name);
  const before = await lstat(target);
  if (!before.isFile() || before.isSymbolicLink() || before.size !== expectedBytes || before.size > maximumArchiveBytes
    || (before.mode & 0o077) !== 0 || before.uid !== process.getuid()) throw new Error(invalidSource);
  const entries = new Map(rows.map(row => [row.path, Object.freeze({ mode: row.mode, bytes: row.bytes, sha256: row.sha256 })]));
  const inspection = new ArchiveInspection(entries, revision);
  const handle = await open(target, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  const buffer = Buffer.allocUnsafe(chunkBytes);
  let offset = 0;
  try {
    const opened = await handle.stat();
    if (!opened.isFile() || opened.dev !== before.dev || opened.ino !== before.ino || opened.size !== before.size) throw new Error(invalidSource);
    while (offset < expectedBytes) {
      const { bytesRead } = await handle.read(buffer, 0, Math.min(buffer.length, expectedBytes - offset), offset);
      if (bytesRead === 0) throw new Error(invalidSource);
      inspection.observe(buffer.subarray(0, bytesRead));
      offset += bytesRead;
    }
    if ((await handle.stat()).size !== expectedBytes || !inspection.complete()) throw new Error(invalidSource);
    const actual = inspection.result();
    if (actual.archiveBytes !== expectedBytes || actual.archiveSha256 !== expectedSha256) throw new Error(invalidSource);
    return actual;
  } finally { await handle.close(); }
}

function waitForChild(child, deadline, onTimeout = () => {}, onStopReady = () => {}) {
  return new Promise(resolve => {
    let settled = false;
    let timedOut = false;
    let stopStartedAt;
    let forceTimer;
    let settleTimer;
    const finish = (code, signal, didSettle) => {
      if (settled) return;
      settled = true;
      clearTimeout(deadlineTimer);
      if (forceTimer) clearTimeout(forceTimer);
      if (settleTimer) clearTimeout(settleTimer);
      resolve(Object.freeze({ code, signal, settled: didSettle, timedOut, stopStartedAt,
        success: didSettle && !timedOut && code === 0 && signal === null }));
    };
    const beginStop = () => {
      if (settled || settleTimer) return;
      stopStartedAt = Date.now();
      child.kill('SIGTERM');
      forceTimer = setTimeout(() => child.kill('SIGKILL'), killGraceMs);
      forceTimer.unref?.();
      settleTimer = setTimeout(() => finish(null, null, false), settlementMs);
    };
    const deadlineTimer = setTimeout(() => {
      timedOut = true;
      onTimeout();
      beginStop();
    }, deadline);
    child.once('close', (code, signal) => finish(code, signal, true));
    child.once('error', () => beginStop());
    onStopReady(beginStop);
  });
}

async function withSettlement(promise, startedAt) {
  const remainingMs = Math.max(0, settlementMs - (Date.now() - startedAt));
  let timer;
  const deadline = new Promise(resolve => { timer = setTimeout(() => resolve(false), remainingMs); });
  const result = await Promise.race([promise, deadline]);
  clearTimeout(timer);
  return result;
}

function unsettledError(primary) {
  const unsettled = new Error('An immutable source process did not settle within its bound.');
  const error = primary
    ? new AggregateError([primary, unsettled], 'Archive processing failed and its child/output did not settle.')
    : unsettled;
  error.preserveOwnedPaths = true;
  return error;
}

class ArchiveInspection {
  constructor(gitEntries, revision) {
    if (typeof revision !== 'string' || !/^[a-f0-9]{40}$/.test(revision)) throw new Error(invalidSource);
    this.expectedGlobalMetadata = Buffer.from(`52 comment=${revision}\n`, 'ascii');
    this.gitEntries = gitEntries;
    this.files = [];
    this.seenPaths = new Set();
    this.directories = new Set();
    this.expectedDirectories = new Set();
    for (const file of gitEntries.keys()) {
      const segments = file.split('/');
      for (let index = 1; index < segments.length; index++) this.expectedDirectories.add(segments.slice(0, index).join('/'));
    }
    this.archiveHash = createHash('sha256');
    this.gitBlobHash = null;
    this.fileHash = null;
    this.fileBytes = 0;
    this.archiveBytes = 0;
    this.expandedBytes = 0;
    this.headerCount = 0;
    this.globalMetadataSeen = false;
    this.block = Buffer.alloc(0);
    this.pendingMetadata = null;
    this.pendingFile = null;
    this.zeroBlocks = 0;
    this.stderrBytes = 0;
    this.pathBytes = 0;
  }

  observe(chunk) {
    this.archiveBytes += chunk.length;
    if (this.archiveBytes > maximumArchiveBytes) throw new Error(invalidSource);
    this.archiveHash.update(chunk);
    this.block = this.block.length === 0 ? Buffer.from(chunk) : Buffer.concat([this.block, chunk]);
    this.consumeBlocks();
  }

  complete() {
    return this.globalMetadataSeen && this.zeroBlocks >= 2 && this.pendingMetadata === null
      && this.pendingFile === null && this.block.length === 0
      && this.archiveBytes % blockBytes === 0 && this.files.length === this.gitEntries.size;
  }

  result() {
    this.files.sort((left, right) => Buffer.compare(Buffer.from(left.path, 'utf8'), Buffer.from(right.path, 'utf8')));
    return Object.freeze({ archiveBytes: this.archiveBytes, archiveSha256: this.archiveHash.digest('hex'),
      files: Object.freeze(this.files), expandedBytes: this.expandedBytes });
  }

  consumeBlocks() {
    while (this.zeroBlocks < 2) {
      if (this.pendingMetadata) {
        if (!this.consumeMetadataPayload()) return;
      } else if (this.pendingFile) {
        if (!this.consumeFilePayload()) return;
      } else {
        if (this.block.length < blockBytes) return;
        this.consumeHeader(this.block.subarray(0, blockBytes));
        this.block = this.block.subarray(blockBytes);
      }
    }
    if (this.block.some(value => value !== 0)) throw new Error(invalidSource);
    this.block = Buffer.alloc(0);
  }

  consumeHeader(header) {
    if (header.every(value => value === 0)) { this.zeroBlocks++; return; }
    if (this.zeroBlocks !== 0 || readTarChecksum(header) !== tarChecksum(header)
      || header.toString('ascii', 257, 263) !== 'ustar\0' || header.toString('ascii', 263, 265) !== '00') throw new Error(invalidSource);
    const mode = readTarOctal(header, 100, 108);
    const size = readTarOctal(header, 124, 136);
    const type = header[156] === 0 ? '0' : String.fromCharCode(header[156]);
    const rawPath = readTarPath(header);
    const pathValue = type === '5' && rawPath.endsWith('/') ? rawPath.slice(0, -1) : rawPath;
    validateArchivePath(pathValue);
    if (type === 'g') {
      if (this.headerCount !== 0 || this.globalMetadataSeen || pathValue !== 'pax_global_header'
        || mode !== 0o666 || size !== this.expectedGlobalMetadata.length) throw new Error(invalidSource);
      this.globalMetadataSeen = true;
      this.headerCount++;
      this.pendingMetadata = { bytes: Buffer.alloc(this.expectedGlobalMetadata.length), count: 0 };
      return;
    }
    if (!this.globalMetadataSeen) throw new Error(invalidSource);
    if (type === '5') {
      if (size !== 0 || this.directories.has(pathValue) || !this.expectedDirectories.has(pathValue)) throw new Error(invalidSource);
      this.directories.add(pathValue);
      this.headerCount++;
      return;
    }
    if (type !== '0' || ![0o644, 0o755].includes(mode) || this.files.length >= maximumFiles || !this.gitEntries.has(pathValue)) {
      throw new Error(invalidSource);
    }
    const expected = this.gitEntries.get(pathValue);
    if (expected.mode !== mode || expected.bytes !== size || this.seenPaths.has(pathValue)) throw new Error(invalidSource);
    this.seenPaths.add(pathValue);
    this.headerCount++;
    if (size === 0) {
      const actualBlobSha = createHash('sha1').update('blob 0\0').digest('hex');
      const actualFileSha = createHash('sha256').digest('hex');
      if ((expected.blobSha && actualBlobSha !== expected.blobSha) || (expected.sha256 && actualFileSha !== expected.sha256)) {
        throw new Error(invalidSource);
      }
      this.addFile(pathValue, mode, 0, actualFileSha);
      return;
    }
    this.pendingFile = { path: pathValue, mode, size, paddedSize: Math.ceil(size / blockBytes) * blockBytes };
    this.fileBytes = 0;
    this.gitBlobHash = createHash('sha1').update(`blob ${size}\0`);
    this.fileHash = createHash('sha256');
  }

  consumeMetadataPayload() {
    const pending = this.pendingMetadata;
    const remaining = blockBytes - pending.count;
    if (this.block.length === 0) return false;
    const count = Math.min(this.block.length, remaining, chunkBytes);
    const contentCount = Math.max(0, Math.min(count, this.expectedGlobalMetadata.length - pending.count));
    if (contentCount > 0) this.block.copy(pending.bytes, pending.count, 0, contentCount);
    if (count > contentCount && this.block.subarray(contentCount, count).some(value => value !== 0)) throw new Error(invalidSource);
    pending.count += count;
    this.block = this.block.subarray(count);
    if (pending.count < blockBytes) return false;
    if (!pending.bytes.equals(this.expectedGlobalMetadata)) throw new Error(invalidSource);
    this.pendingMetadata = null;
    return true;
  }

  consumeFilePayload() {
    const remaining = this.pendingFile.paddedSize - this.fileBytes;
    if (this.block.length === 0) return false;
    const count = Math.min(this.block.length, remaining, chunkBytes);
    const part = this.block.subarray(0, count);
    const contentRemaining = Math.max(0, this.pendingFile.size - this.fileBytes);
    const contentCount = Math.min(count, contentRemaining);
    if (contentCount > 0) {
      const content = part.subarray(0, contentCount);
      this.gitBlobHash.update(content);
      this.fileHash.update(content);
    }
    if (count > contentCount && part.subarray(contentCount).some(value => value !== 0)) throw new Error(invalidSource);
    this.fileBytes += count;
    this.block = this.block.subarray(count);
    if (this.fileBytes < this.pendingFile.paddedSize) return false;
    const expected = this.gitEntries.get(this.pendingFile.path);
    const actualBlobSha = this.gitBlobHash.digest('hex');
    const actualFileSha = this.fileHash.digest('hex');
    if ((expected.blobSha && actualBlobSha !== expected.blobSha) || (expected.sha256 && actualFileSha !== expected.sha256)) {
      throw new Error(invalidSource);
    }
    this.addFile(this.pendingFile.path, this.pendingFile.mode, this.pendingFile.size, actualFileSha);
    this.pendingFile = null;
    this.gitBlobHash = null;
    this.fileHash = null;
    return true;
  }

  addFile(filePath, mode, size, sha256) {
    const nameBytes = Buffer.byteLength(filePath, 'utf8');
    this.pathBytes += nameBytes;
    this.expandedBytes += size;
    if (this.pathBytes > 16 * 1024 * 1024 || this.expandedBytes > maximumArchiveBytes) throw new Error(invalidSource);
    this.files.push(Object.freeze({ path: filePath, mode, bytes: size, sha256 }));
  }

}

function readTarPath(header) {
  const name = readTarText(header, 0, 100);
  const prefix = readTarText(header, 345, 500);
  return prefix ? `${prefix}/${name}` : name;
}

function readTarText(bytes, start, end) {
  const field = bytes.subarray(start, end);
  const nullIndex = field.indexOf(0);
  const content = nullIndex < 0 ? field : field.subarray(0, nullIndex);
  if (nullIndex >= 0 && field.subarray(nullIndex).some(value => value !== 0)) throw new Error(invalidSource);
  return new TextDecoder('utf-8', { fatal: true }).decode(content);
}

function readTarOctal(header, start, end) {
  const raw = header.toString('ascii', start, end).replace(/[\0 ]+$/g, '').replace(/^ +/g, '');
  if (!/^[0-7]+$/.test(raw)) throw new Error(invalidSource);
  const value = Number.parseInt(raw, 8);
  if (!Number.isSafeInteger(value) || value < 0) throw new Error(invalidSource);
  return value;
}

function readTarChecksum(header) { return readTarOctal(header, 148, 156); }

function tarChecksum(header) {
  let sum = 0;
  for (let index = 0; index < header.length; index++) sum += index >= 148 && index < 156 ? 32 : header[index];
  return sum;
}

function validateArchivePath(value) {
  const name = value.endsWith('/') ? value.slice(0, -1) : value;
  if (!name || name.startsWith('/') || name.includes('\\') || Buffer.byteLength(name, 'utf8') > maximumPathBytes
    || name.split('/').length > maximumDepth || name.split('/').some(part => part === '' || part === '.' || part === '..')) {
    throw new Error(invalidSource);
  }
}
