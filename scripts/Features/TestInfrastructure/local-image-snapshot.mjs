import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { chmod, lstat, mkdir, mkdtemp, open, rmdir, unlink } from 'node:fs/promises';
import path from 'node:path';
import { enumerateBuildInputs } from './local-image-context.mjs';
import { localImage, messages } from './local-image-contracts.mjs';

const ownedSnapshots = new WeakMap();
const directoryKind = 1;
const fileKind = 2;
const temporaryDirectoryMode = 0o700;
const temporaryFileMode = 0o600;
const readFlags = constants.O_RDONLY | constants.O_NOFOLLOW;
const writeFlags = constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL | constants.O_NOFOLLOW;

export async function readBuildInputs(root) {
  const inputs = await enumerateBuildInputs(root);
  const digest = createHash('sha256');
  let copiedBytes = 0;
  for (const entry of inputs.entries) {
    appendHeader(digest, entry);
    if (entry.kind === 'file') {
      const result = await streamSourceFile(entry, digest);
      if (entry.controlDigest !== null && result.digest !== entry.controlDigest) {
        throw new Error(messages.contextChanged);
      }
      copiedBytes += result.bytes;
    }
  }
  if (copiedBytes !== inputs.bytes) throw new Error(messages.contextChanged);
  await verifySourceDirectories([...inputs.entries, ...inputs.scaffolds]);
  const scaffolds = Object.freeze(inputs.scaffolds.map(entry => Object.freeze({ relative: entry.relative,
    mode: entry.mode, dev: entry.stat.dev, ino: entry.stat.ino })));
  return Object.freeze({ digest: `sha256:${digest.digest('hex')}`, bases: inputs.bases,
    files: inputs.files, bytes: inputs.bytes, scaffolds });
}

export async function createBuildContextSnapshot(root) {
  const inputs = await enumerateBuildInputs(root);
  const parent = await ensureSnapshotParent(root);
  const directory = await mkdtemp(path.join(parent, 'context-'));
  const state = { directory, entries: new Map(), directories: [], files: [], cleaned: false };
  try {
    const createdRoot = await lstat(directory);
    if (!createdRoot.isDirectory() || createdRoot.isSymbolicLink()) throw new Error(messages.contextChanged);
    const rootEntry = Object.freeze({ relative: '', path: directory, kind: 'directory',
      dev: createdRoot.dev, ino: createdRoot.ino });
    state.entries.set('', rootEntry);
    state.directories.push(rootEntry);
    await chmod(directory, temporaryDirectoryMode);
    const checkedRoot = await lstat(directory);
    if (!checkedDirectory(checkedRoot, createdRoot)) throw new Error(messages.contextChanged);

    const digest = createHash('sha256');
    let copiedBytes = 0;
    for (const entry of inputs.scaffolds) await createSnapshotScaffold(entry, directory, state);
    for (const entry of inputs.entries) {
      if (entry.kind === 'directory') await verifySourceDirectory(entry);
      appendHeader(digest, entry);
      const target = path.join(directory, ...entry.relative.split('/'));
      await verifyOwnedParent(entry.relative, state);
      if (entry.kind === 'directory') {
        await createSnapshotDirectory(entry, target, state);
      } else {
        const copied = await copySnapshotFile(entry, target, digest, state);
        if (entry.controlDigest !== null && copied.digest !== entry.controlDigest) {
          throw new Error(messages.contextChanged);
        }
        copiedBytes += copied.bytes;
      }
    }
    if (copiedBytes !== inputs.bytes) throw new Error(messages.contextChanged);
    await verifySourceDirectories([...inputs.entries, ...inputs.scaffolds]);
    for (const source of inputs.entries.filter(entry => entry.kind === 'directory').reverse()) {
      const entry = state.entries.get(source.relative);
      if (!entry) throw new Error(messages.contextChanged);
      await setOwnedDirectoryMode(entry, source.mode, state);
    }
    for (const source of [...inputs.scaffolds].reverse()) {
      const entry = state.entries.get(source.relative);
      if (!entry) throw new Error(messages.contextChanged);
      await setOwnedDirectoryMode(entry, source.mode, state);
    }
    const snapshotScaffolds = inputs.scaffolds.map(entry => {
      const actual = state.entries.get(entry.relative);
      if (!actual) throw new Error(messages.contextChanged);
      return Object.freeze({ relative: actual.relative, mode: entry.mode, dev: actual.dev, ino: actual.ino });
    });
    const snapshot = Object.freeze({ directory, digest: `sha256:${digest.digest('hex')}`,
      bases: inputs.bases, files: inputs.files, bytes: inputs.bytes, scaffolds: Object.freeze(snapshotScaffolds) });
    ownedSnapshots.set(snapshot, state);
    return snapshot;
  } catch (primary) {
    const failures = [primary];
    if (state.entries.has('')) await captureFailure(() => cleanupState(state), failures);
    else await captureFailure(() => rmdir(directory), failures);
    throwFailures(failures);
  }
}

export async function cleanupBuildContextSnapshot(snapshot) {
  const state = ownedSnapshots.get(snapshot);
  if (!state) throw new Error(messages.invalidContext);
  if (state.cleaned) return;
  await cleanupState(state);
  state.cleaned = true;
  ownedSnapshots.delete(snapshot);
}

async function ensureSnapshotParent(root) {
  root = path.resolve(root);
  const rootStat = await lstat(root);
  if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) throw new Error(messages.invalidReceiptPath);
  const parent = path.resolve(root, localImage.receiptDirectory);
  const relative = path.relative(root, parent);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) throw new Error(messages.invalidReceiptPath);
  let current = root;
  for (const segment of relative.split(path.sep).filter(Boolean)) {
    current = path.join(current, segment);
    await mkdir(current, { mode: temporaryDirectoryMode }).catch(error => {
      if (error?.code !== 'EEXIST') throw error;
    });
    const info = await lstat(current);
    if (!info.isDirectory() || info.isSymbolicLink()) throw new Error(messages.invalidReceiptPath);
  }
  return parent;
}

async function createSnapshotDirectory(entry, target, state) {
  await mkdir(target, { mode: temporaryDirectoryMode });
  const created = await lstat(target);
  if (!created.isDirectory() || created.isSymbolicLink()) throw new Error(messages.contextChanged);
  const identity = Object.freeze({ relative: entry.relative, path: target, kind: 'directory',
    dev: created.dev, ino: created.ino });
  state.entries.set(entry.relative, identity);
  state.directories.push(identity);
  if ((created.mode & 0o777) !== temporaryDirectoryMode) {
    await chmod(target, temporaryDirectoryMode);
    const checked = await lstat(target);
    if (!checkedDirectory(checked, created) || (checked.mode & 0o777) !== temporaryDirectoryMode) {
      throw new Error(messages.contextChanged);
    }
  }
}

async function createSnapshotScaffold(entry, root, state) {
  const target = path.join(root, ...entry.relative.split('/'));
  const parent = path.posix.dirname(entry.relative);
  const parentKey = parent === '.' ? '' : parent;
  await verifyOwnedParent(entry.relative, state);
  await mkdir(target, { mode: temporaryDirectoryMode });
  const created = await lstat(target);
  if (!created.isDirectory() || created.isSymbolicLink()) throw new Error(messages.contextChanged);
  const identity = Object.freeze({ relative: entry.relative, path: target, kind: 'directory',
    dev: created.dev, ino: created.ino, scaffold: true });
  state.entries.set(entry.relative, identity);
  state.directories.push(identity);
  await chmod(target, temporaryDirectoryMode);
  const restored = await lstat(target);
  if (!checkedDirectory(restored, created) || (restored.mode & 0o777) !== temporaryDirectoryMode) {
    throw new Error(messages.contextChanged);
  }
  const parentEntry = state.entries.get(parentKey);
  if (!parentEntry || !checkedDirectory(await lstat(parentEntry.path), parentEntry)) {
    throw new Error(messages.contextChanged);
  }
}

async function copySnapshotFile(entry, target, digest, state) {
  let source;
  let destination;
  let stream;
  const failures = [];
  let bytes = 0;
  const fileDigest = createHash('sha256');
  try {
    source = await open(entry.absolute, readFlags);
    const opened = await source.stat();
    if (!opened.isFile() || !sameStat(entry.stat, opened)) throw new Error(messages.contextChanged);
    destination = await open(target, writeFlags, temporaryFileMode);
    const output = await destination.stat();
    if (!output.isFile()) throw new Error(messages.contextChanged);
    const identity = Object.freeze({ relative: entry.relative, path: target, kind: 'file', dev: output.dev, ino: output.ino });
    state.entries.set(entry.relative, identity);
    state.files.push(identity);
    stream = source.createReadStream({ autoClose: false });
    for await (const chunk of stream) {
      bytes += chunk.length;
      if (bytes > entry.size || bytes > localImage.maxInputFileBytes) throw new Error(messages.contextChanged);
      digest.update(chunk);
      fileDigest.update(chunk);
      await writeAll(destination, chunk);
    }
    if (bytes !== entry.size) throw new Error(messages.contextChanged);
    const afterHandle = await source.stat();
    const afterPath = await lstat(entry.absolute);
    const afterOutput = await destination.stat();
    if (!sameStat(entry.stat, afterHandle) || !sameStat(entry.stat, afterPath)
      || afterOutput.size !== bytes || !sameIdentity(output, afterOutput)) throw new Error(messages.contextChanged);
    await destination.sync();
    await destination.close();
    destination = null;
    await verifyOwnedParent(entry.relative, state);
    await chmod(target, entry.mode);
    const finalOutput = await lstat(target);
    if (!finalOutput.isFile() || finalOutput.isSymbolicLink() || (finalOutput.mode & 0o777) !== entry.mode
      || !sameIdentity(output, finalOutput) || finalOutput.size !== bytes) throw new Error(messages.contextChanged);
  } catch (error) {
    failures.push(mapInputError(error));
  }
  if (stream && !stream.destroyed) stream.destroy();
  if (destination) await captureFailure(() => destination.close(), failures);
  if (source) await captureFailure(() => source.close(), failures);
  throwFailures(failures);
  return Object.freeze({ bytes, digest: fileDigest.digest('hex') });
}

async function streamSourceFile(entry, digest) {
  let handle;
  let stream;
  const failures = [];
  const fileDigest = createHash('sha256');
  let bytes = 0;
  try {
    handle = await open(entry.absolute, readFlags);
    const before = await handle.stat();
    if (!before.isFile() || !sameStat(entry.stat, before)) throw new Error(messages.contextChanged);
    stream = handle.createReadStream({ autoClose: false });
    for await (const chunk of stream) {
      bytes += chunk.length;
      if (bytes > entry.size || bytes > localImage.maxInputFileBytes) throw new Error(messages.contextChanged);
      digest.update(chunk);
      fileDigest.update(chunk);
    }
    const afterHandle = await handle.stat();
    const afterPath = await lstat(entry.absolute);
    if (bytes !== entry.size || !sameStat(entry.stat, afterHandle) || !sameStat(entry.stat, afterPath)) {
      throw new Error(messages.contextChanged);
    }
  } catch (error) {
    failures.push(mapInputError(error));
  }
  if (stream && !stream.destroyed) stream.destroy();
  if (handle) await captureFailure(() => handle.close(), failures);
  throwFailures(failures);
  return Object.freeze({ bytes, digest: fileDigest.digest('hex') });
}

async function verifySourceDirectories(entries) {
  for (const entry of entries) {
    if (entry.kind === 'directory') await verifySourceDirectory(entry);
  }
}

export function sameScaffolds(actual, expected) {
  return actual.length === expected.length && actual.every((entry, index) =>
    entry.relative === expected[index].relative && entry.mode === expected[index].mode
      && entry.dev === expected[index].dev && entry.ino === expected[index].ino);
}

async function verifySourceDirectory(entry) {
  const actual = await lstat(entry.absolute);
  if (!actual.isDirectory() || actual.isSymbolicLink() || !sameStat(entry.stat, actual)) {
    throw new Error(messages.contextChanged);
  }
}

async function writeAll(handle, buffer) {
  let offset = 0;
  while (offset < buffer.length) {
    const result = await handle.write(buffer, offset, buffer.length - offset, null);
    if (result.bytesWritten <= 0) throw new Error(messages.contextChanged);
    offset += result.bytesWritten;
  }
}

async function verifyOwnedParent(relative, state) {
  const parent = path.posix.dirname(relative);
  const key = parent === '.' ? '' : parent;
  const expected = state.entries.get(key);
  if (!expected) throw new Error(messages.contextChanged);
  const actual = await lstat(expected.path);
  if (!checkedDirectory(actual, expected)) throw new Error(messages.contextChanged);
}

async function setOwnedDirectoryMode(entry, mode, state) {
  await verifyOwnedParent(entry.relative, state);
  const actual = await lstat(entry.path);
  if (!checkedDirectory(actual, entry)) throw new Error(messages.contextChanged);
  await chmod(entry.path, mode);
  const restored = await lstat(entry.path);
  if (!checkedDirectory(restored, entry) || (restored.mode & 0o777) !== mode) {
    throw new Error(messages.contextChanged);
  }
}

async function cleanupState(state) {
  const failures = [];
  let root;
  try { root = await lstat(state.directory); }
  catch (error) {
    if (error?.code === 'ENOENT') return;
    failures.push(error);
  }
  const expectedRoot = state.entries.get('');
  if (!root) {
    throwFailures(failures);
    return;
  }
  if (!expectedRoot || !checkedDirectory(root, expectedRoot)) {
    throw new Error(messages.invalidContext);
  }
  const safeDirectories = [];
  let unsafeDirectory = false;
  for (const entry of state.directories) {
    try {
      const actual = await lstat(entry.path);
      if (!checkedDirectory(actual, entry)) throw new Error(messages.invalidContext);
      safeDirectories.push(entry);
      await chmod(entry.path, temporaryDirectoryMode);
    } catch (error) {
      unsafeDirectory = true;
      failures.push(error);
      break;
    }
  }
  const safeFiles = [];
  if (!unsafeDirectory) {
    for (const entry of state.files) {
      try {
        const actual = await lstat(entry.path);
        if (!actual.isFile() || actual.isSymbolicLink() || !sameIdentity(entry, actual)) {
          throw new Error(messages.invalidContext);
        }
        safeFiles.push(entry);
      } catch (error) { failures.push(error); }
    }
    for (const entry of safeFiles.reverse()) await captureFailure(() => unlink(entry.path), failures);
  }
  if (unsafeDirectory) throwFailures(failures);
  for (const entry of safeDirectories.slice(1).reverse()) await captureFailure(() => rmdir(entry.path), failures);
  if (safeDirectories.some(entry => entry.relative === '')) {
    await captureFailure(() => rmdir(state.directory), failures);
  }
  throwFailures(failures);
}

function checkedDirectory(actual, expected) {
  return actual.isDirectory() && !actual.isSymbolicLink() && sameIdentity(actual, expected);
}

function appendHeader(digest, entry) {
  digest.update(frame(entry.relative));
  digest.update(Buffer.from([entry.kind === 'directory' ? directoryKind : fileKind]));
  digest.update(uint32(entry.mode));
  digest.update(uint64(entry.kind === 'directory' ? 0 : entry.size));
}

function frame(value) {
  const bytes = Buffer.from(value, 'utf8');
  return Buffer.concat([uint32(bytes.length), bytes]);
}

function uint32(value) {
  const bytes = Buffer.allocUnsafe(4);
  bytes.writeUInt32BE(value);
  return bytes;
}

function uint64(value) {
  const bytes = Buffer.allocUnsafe(8);
  bytes.writeBigUInt64BE(BigInt(value));
  return bytes;
}

function sameIdentity(left, right) { return left.dev === right.dev && left.ino === right.ino; }

function sameStat(left, right) {
  return sameIdentity(left, right) && left.size === right.size
    && (left.mode & 0o777) === (right.mode & 0o777) && left.mtimeMs === right.mtimeMs && left.ctimeMs === right.ctimeMs;
}

function mapInputError(error) {
  if (error?.message === messages.contextChanged || error?.message === messages.invalidContext) return error;
  return new Error(messages.invalidContext);
}

async function captureFailure(action, failures) {
  try { await action(); }
  catch (error) { failures.push(error); }
}

function throwFailures(failures) {
  if (failures.length === 1) throw failures[0];
  if (failures.length > 1) throw new AggregateError(failures, messages.imageCleanup);
}
