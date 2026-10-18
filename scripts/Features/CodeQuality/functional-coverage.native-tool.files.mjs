import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { assertAbsolutePath, fail } from './functional-coverage.native-tool.contracts.mjs';

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
      fail('Symbolic links are not accepted in native tool inputs.');
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

export function assertDirectoryPath(directory, allowMissingFinal, maximumPathCharacters) {
  const normalized = assertAbsolutePath(directory, maximumPathCharacters, 'An native tool input directory path is invalid.');
  const parent = path.dirname(normalized);
  assertNoSymlinkAncestors(parent, true);
  const statPath = allowMissingFinal ? parent : normalized;
  const stat = fs.lstatSync(statPath, { bigint: true });
  if (!stat.isDirectory() || stat.isSymbolicLink()) {
    fail('An native tool input directory is invalid.');
  }
  return normalized;
}

export function readFileMetadata(filePath, maximumBytes, message, bounds) {
  const hash = hashBoundedRegularFile(filePath, maximumBytes, message, bounds);
  return { mode: hash.mode, length: hash.length, sha256: hash.digest };
}

