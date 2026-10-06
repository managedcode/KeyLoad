import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { assertAbsolutePath, fail, format } from './functional-coverage.server-image-contracts.mjs';

export function assertNoSymlinkAncestors(filePath, includeFinal) {
  const absolute = path.resolve(filePath);
  const parts = absolute.split(path.sep).filter(Boolean);
  let current = path.parse(absolute).root;
  for (let index = 0; index < parts.length; index += 1) {
    current = path.join(current, parts[index]);
    if (!includeFinal && index === parts.length - 1) {
      break;
    }
    let stat;
    try {
      stat = fs.lstatSync(current, { bigint: true });
    } catch {
      fail('An input path component is missing.');
    }
    if (stat.isSymbolicLink()) {
      fail('Symbolic links are not accepted in image inputs.');
    }
  }
}

export function sameFileIdentity(left, right) {
  return left.dev === right.dev && left.ino === right.ino && left.size === right.size &&
    left.mtimeNs === right.mtimeNs && left.ctimeNs === right.ctimeNs;
}

function closeDescriptors(descriptors) {
  const failures = [];
  for (let index = descriptors.length - 1; index >= 0; index -= 1) {
    try {
      fs.closeSync(descriptors[index]);
    } catch (error) {
      failures.push(error);
    }
  }
  if (failures.length === 1) {
    throw failures[0];
  }
  if (failures.length > 1) {
    throw new AggregateError(failures, 'Multiple native coverage file handles failed to close.');
  }
}

function runWithCleanup(action, cleanup, message) {
  let result;
  let primaryError;
  try {
    result = action();
  } catch (error) {
    primaryError = error;
  }
  let cleanupError;
  try {
    cleanup();
  } catch (error) {
    cleanupError = error;
  }
  if (primaryError && cleanupError) {
    throw new AggregateError([primaryError, cleanupError], message);
  }
  if (primaryError) {
    throw primaryError;
  }
  if (cleanupError) {
    throw cleanupError;
  }
  return result;
}

export function readBoundedRegularFile(filePath, maximumBytes, message) {
  assertNoSymlinkAncestors(filePath, true);
  const flags = fs.constants.O_RDONLY | (fs.constants.O_NOFOLLOW ?? 0);
  const descriptors = [];
  return runWithCleanup(() => {
    try {
      const descriptor = fs.openSync(filePath, flags);
      descriptors.push(descriptor);
      const before = fs.fstatSync(descriptor, { bigint: true });
      if (!before.isFile() || before.size < 0n || before.size > BigInt(maximumBytes)) {
        fail(message);
      }
      const length = Number(before.size);
      const bytes = Buffer.alloc(length);
      let offset = 0;
      while (offset < length) {
        const read = fs.readSync(descriptor, bytes, offset, length - offset, offset);
        if (read === 0) {
          fail(message);
        }
        offset += read;
      }
      const extra = Buffer.alloc(1);
      if (fs.readSync(descriptor, extra, 0, 1, length) !== 0) {
        fail(message);
      }
      const after = fs.fstatSync(descriptor, { bigint: true });
      const pathAfter = fs.lstatSync(filePath, { bigint: true });
      if (!sameFileIdentity(before, after) || !sameFileIdentity(after, pathAfter)) {
        fail(message);
      }
      return { bytes, mode: Number(before.mode & 0o777n), length, links: Number(before.nlink) };
    } catch (error) {
      if (error instanceof Error && error.message === message) {
        throw error;
      }
      fail(message);
    }
  }, () => closeDescriptors(descriptors), 'Native coverage file read and close failed.');
}

export function hashBoundedRegularFile(filePath, maximumBytes, message, bounds, algorithm = 'sha256') {
  assertNoSymlinkAncestors(filePath, true);
  const flags = fs.constants.O_RDONLY | (fs.constants.O_NOFOLLOW ?? 0);
  const descriptors = [];
  return runWithCleanup(() => {
    try {
      const descriptor = fs.openSync(filePath, flags);
      descriptors.push(descriptor);
      const before = fs.fstatSync(descriptor, { bigint: true });
      if (!before.isFile() || before.size < 0n || before.size > BigInt(maximumBytes)) {
        fail(message);
      }
      const digest = crypto.createHash(algorithm);
      const buffer = Buffer.alloc(bounds.readBufferBytes);
      let bytesRead = 0n;
      for (;;) {
        const read = fs.readSync(descriptor, buffer, 0, buffer.length, Number(bytesRead));
        if (read === 0) {
          break;
        }
        bytesRead += BigInt(read);
        if (bytesRead > BigInt(maximumBytes)) {
          fail(message);
        }
        digest.update(buffer.subarray(0, read));
      }
      const after = fs.fstatSync(descriptor, { bigint: true });
      const pathAfter = fs.lstatSync(filePath, { bigint: true });
      if (bytesRead !== before.size || !sameFileIdentity(before, after) || !sameFileIdentity(after, pathAfter)) {
        fail(message);
      }
      return { digest: digest.digest('hex'), length: Number(bytesRead), mode: Number(before.mode & 0o777n) };
    } catch (error) {
      if (error instanceof Error && error.message === message) {
        throw error;
      }
      fail(message);
    }
  }, () => closeDescriptors(descriptors), 'Native coverage file hash and close failed.');
}

export function copyBoundedRegularFile(sourcePath, destinationPath, bounds, aggregate) {
  if (aggregate.fileCount >= bounds.maximumFiles) {
    fail('Image input inventory exceeds the admitted entry bound.');
  }
  assertNoSymlinkAncestors(sourcePath, true);
  const sourceFlags = fs.constants.O_RDONLY | (fs.constants.O_NOFOLLOW ?? 0);
  const destinationFlags = fs.constants.O_WRONLY | fs.constants.O_CREAT | fs.constants.O_EXCL |
    (fs.constants.O_NOFOLLOW ?? 0);
  const descriptors = [];
  return runWithCleanup(() => {
    const input = fs.openSync(sourcePath, sourceFlags);
    descriptors.push(input);
    const before = fs.fstatSync(input, { bigint: true });
    if (!before.isFile() || before.size < 0n || before.size > BigInt(bounds.maximumFileBytes)) {
      fail('An image input exceeds its admitted file bound.');
    }
    const output = fs.openSync(destinationPath, destinationFlags, Number(before.mode & 0o777n));
    descriptors.push(output);
    const digest = crypto.createHash('sha256');
    const total = copyBoundedChunks(input, output, bounds, aggregate, digest);
    const after = fs.fstatSync(input, { bigint: true });
    const pathAfter = fs.lstatSync(sourcePath, { bigint: true });
    if (total !== before.size || !sameFileIdentity(before, after) || !sameFileIdentity(after, pathAfter)) {
      fail('An image input changed while it was being materialized.');
    }
    fs.fchmodSync(output, Number(before.mode & 0o777n));
    fs.fsyncSync(output);
    aggregate.fileCount += 1;
    return { length: Number(total), mode: Number(before.mode & 0o777n), sha256: digest.digest('hex') };
  }, () => closeDescriptors(descriptors), 'Native coverage copy and handle close failed.');
}

function copyBoundedChunks(input, output, bounds, aggregate, digest) {
  const buffer = Buffer.alloc(bounds.readBufferBytes);
  let total = 0n;
  for (;;) {
    const read = fs.readSync(input, buffer, 0, buffer.length, Number(total));
    if (read === 0) {
      return total;
    }
    total += BigInt(read);
    aggregate.totalBytes += BigInt(read);
    if (total > BigInt(bounds.maximumFileBytes) || aggregate.totalBytes > BigInt(bounds.maximumTotalBytes)) {
      fail('Image input bytes exceed the admitted context bounds.');
    }
    digest.update(buffer.subarray(0, read));
    writeAll(output, buffer, read);
  }
}

function writeAll(output, buffer, length) {
  let offset = 0;
  while (offset < length) {
    offset += fs.writeSync(output, buffer, offset, length - offset);
  }
}

export function walkRegularTree(root, relativeRoot, bounds, aggregate) {
  const rootStat = fs.lstatSync(root, { bigint: true });
  if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) {
    fail('An image input root is not a regular directory.');
  }
  const entries = [];
  const pending = [{ absolute: root, relative: relativeRoot }];
  const visited = { entries: 0 };
  while (pending.length > 0) {
    const current = pending.pop();
    scanDirectory(current, pending, entries, visited, bounds, aggregate);
  }
  return entries.sort((left, right) => left.relativePath.localeCompare(right.relativePath, 'en'));
}

function scanDirectory(current, pending, entries, visited, bounds, aggregate) {
  const directory = fs.opendirSync(current.absolute);
  runWithCleanup(() => {
    for (let child = directory.readSync(); child !== null; child = directory.readSync()) {
      visited.entries += 1;
      if (visited.entries > bounds.maximumFiles) {
        fail('Image input inventory exceeds the admitted entry bound.');
      }
      inspectEntry(current, child.name, pending, entries, bounds, aggregate);
    }
  }, () => directory.closeSync(), 'Native coverage directory scan and close failed.');
}

function inspectEntry(current, name, pending, entries, bounds, aggregate) {
  const absolute = path.join(current.absolute, name);
  const relative = path.posix.join(current.relative, name);
  validateWalkPath(absolute, relative, bounds);
  const stat = fs.lstatSync(absolute, { bigint: true });
  if (stat.isSymbolicLink()) {
    fail('Symbolic links are not accepted in image inputs.');
  }
  if (stat.isDirectory()) {
    pending.push({ absolute, relative });
    return;
  }
  appendRegularFile(absolute, relative, stat, entries, bounds, aggregate);
}

function validateWalkPath(absolute, relative, bounds) {
  if (relative.length > bounds.maximumPathCharacters || absolute.length > bounds.maximumPathCharacters ||
      relative.includes('\\') || relative.split('/').includes('..')) {
    fail('An image input path is outside the admitted path bounds.');
  }
}

function appendRegularFile(absolute, relative, stat, entries, bounds, aggregate) {
  if (!stat.isFile() || stat.size < 0n || stat.size > BigInt(bounds.maximumFileBytes)) {
    fail('An image input is not a bounded regular file.');
  }
  const identity = hashBoundedRegularFile(absolute, bounds.maximumFileBytes,
    'An image input changed while its inventory was read.', bounds);
  if (BigInt(identity.length) !== stat.size || identity.mode !== Number(stat.mode & 0o777n)) {
    fail('An image input changed while its inventory was read.');
  }
  aggregate.fileCount += 1;
  aggregate.totalBytes += stat.size;
  if (aggregate.fileCount > bounds.maximumFiles || aggregate.totalBytes > BigInt(bounds.maximumTotalBytes)) {
    fail('Image input inventory exceeds the admitted context bounds.');
  }
  entries.push({ sourcePath: absolute, relativePath: relative, size: Number(stat.size), mode: identity.mode, sha256: identity.digest });
}

export function assertDirectoryPath(directory, allowMissingFinal, maximumPathCharacters) {
  const normalized = assertAbsolutePath(directory, maximumPathCharacters, 'An image input directory path is invalid.');
  const parent = path.dirname(normalized);
  assertNoSymlinkAncestors(parent, true);
  const statPath = allowMissingFinal ? parent : normalized;
  const stat = fs.lstatSync(statPath, { bigint: true });
  if (!stat.isDirectory() || stat.isSymbolicLink()) {
    fail('An image input directory is invalid.');
  }
  return normalized;
}

export function readFileMetadata(filePath, maximumBytes, message, bounds) {
  const hash = hashBoundedRegularFile(filePath, maximumBytes, message, bounds);
  return { mode: hash.mode, length: hash.length, sha256: hash.digest };
}

export function appendInventory(inventory, destinationRelativePath, metadata) {
  inventory.push({ path: destinationRelativePath, mode: metadata.mode, length: metadata.length, sha256: metadata.sha256 });
}

export function makeParentDirectories(root, relativePath) {
  const parent = path.dirname(relativePath);
  if (parent === '.') {
    return;
  }
  const directory = path.join(root, ...parent.split('/'));
  fs.mkdirSync(directory, { recursive: true, mode: 0o755 });
}

export function writeCreateOnly(destinationPath, bytes, mode, bounds, aggregate) {
  if (aggregate.fileCount >= bounds.maximumFiles || bytes.length > bounds.maximumFileBytes ||
      aggregate.totalBytes + BigInt(bytes.length) > BigInt(bounds.maximumTotalBytes)) {
    fail('Generated image metadata exceeds the admitted context bounds.');
  }
  const descriptors = [];
  runWithCleanup(() => {
    const descriptor = fs.openSync(destinationPath,
      fs.constants.O_WRONLY | fs.constants.O_CREAT | fs.constants.O_EXCL | (fs.constants.O_NOFOLLOW ?? 0), mode);
    descriptors.push(descriptor);
    let offset = 0;
    while (offset < bytes.length) {
      offset += fs.writeSync(descriptor, bytes, offset, bytes.length - offset);
    }
    fs.fsyncSync(descriptor);
  }, () => closeDescriptors(descriptors), 'Generated metadata write and close failed.');
  aggregate.fileCount += 1;
  aggregate.totalBytes += BigInt(bytes.length);
  if (aggregate.fileCount > bounds.maximumFiles || aggregate.totalBytes > BigInt(bounds.maximumTotalBytes)) {
    fail('Generated image metadata exceeds the admitted context bounds.');
  }
  return { length: bytes.length, mode, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
}
