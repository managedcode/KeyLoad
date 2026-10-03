import { constants } from 'node:fs';
import { lstat, mkdir, open } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { bundleError, bundleLimit, requireBundle } from './image-bundle-contract.mjs';

export async function requireDirectory(directory) {
  requireBundle(typeof directory === 'string' && path.isAbsolute(directory)
    && !/[\x00-\x1f\x7f]/.test(directory), bundleError.path);
  const resolved = path.resolve(directory);
  let current = path.parse(resolved).root;
  for (const component of resolved.slice(current.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, component);
    const info = await lstat(current).catch(() => null);
    requireBundle(info?.isDirectory() && !info.isSymbolicLink(), bundleError.path);
  }
  return resolved;
}

export async function createDirectory(directory) {
  await requireDirectory(path.dirname(directory));
  await mkdir(directory, { mode: 0o700 }).catch(error => {
    throw new Error(error?.code === 'EEXIST' ? bundleError.exists : bundleError.path);
  });
  return requireDirectory(directory);
}

export async function requireAbsent(target) {
  await requireDirectory(path.dirname(target));
  const entry = await lstat(target).catch(error => {
    if (error?.code === 'ENOENT') return null;
    throw new Error(bundleError.path);
  });
  requireBundle(entry === null, bundleError.exists);
}

export async function requireOutputFile(target) {
  await requireDirectory(path.dirname(target));
  const info = await lstat(target).catch(() => null);
  requireBundle(info?.isFile() && !info.isSymbolicLink(), bundleError.path);
}

export async function writeExclusive(target, bytes) {
  requireBundle(Buffer.isBuffer(bytes) && bytes.length <= bundleLimit.jsonBytes);
  await requireDirectory(path.dirname(target));
  const handle = await open(target, 'wx', 0o600).catch(error => {
    throw new Error(error?.code === 'EEXIST' ? bundleError.exists : bundleError.path);
  });
  try {
    await handle.writeFile(bytes);
    await handle.sync();
  } finally {
    await handle.close();
  }
}

export async function reserveArchive(target) {
  await writeExclusive(target, Buffer.alloc(0));
}

async function openRegular(target, maximumBytes) {
  await requireDirectory(path.dirname(target));
  const before = await lstat(target).catch(() => null);
  requireBundle(before?.isFile() && !before.isSymbolicLink()
    && before.size > 0 && before.size <= maximumBytes, bundleError.path);
  const handle = await open(target, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0)).catch(() => {
    throw new Error(bundleError.path);
  });
  try {
    const info = await handle.stat();
    requireBundle(info.isFile() && sameFile(before, info), bundleError.path);
    return { handle, info };
  } catch (error) {
    await handle.close();
    throw error;
  }
}

function sameFile(left, right) {
  return left.dev === right.dev && left.ino === right.ino && left.size === right.size
    && left.mtimeMs === right.mtimeMs && left.ctimeMs === right.ctimeMs;
}

async function requireUnchanged(target, opened) {
  const after = await lstat(target).catch(() => null);
  requireBundle(after?.isFile() && !after.isSymbolicLink() && sameFile(opened.info, after)
    && sameFile(opened.info, await opened.handle.stat()), bundleError.path);
}

export async function readRegular(target) {
  const opened = await openRegular(target, bundleLimit.jsonBytes);
  try {
    const buffer = Buffer.allocUnsafe(opened.info.size + 1);
    let length = 0;
    while (length < buffer.length) {
      const read = await opened.handle.read(buffer, length, buffer.length - length, length);
      if (read.bytesRead === 0) break;
      length += read.bytesRead;
    }
    await requireUnchanged(target, opened);
    requireBundle(length === opened.info.size);
    return buffer.subarray(0, length);
  } finally {
    await opened.handle.close();
  }
}

export async function hashArchive(target) {
  const opened = await openRegular(target, bundleLimit.archiveBytes);
  try {
    const hash = createHash('sha256');
    const chunk = Buffer.allocUnsafe(bundleLimit.chunkBytes);
    let bytes = 0;
    while (true) {
      const read = await opened.handle.read(chunk, 0, chunk.length, bytes);
      if (read.bytesRead === 0) break;
      bytes += read.bytesRead;
      requireBundle(bytes <= bundleLimit.archiveBytes);
      hash.update(chunk.subarray(0, read.bytesRead));
    }
    await requireUnchanged(target, opened);
    requireBundle(bytes === opened.info.size);
    return Object.freeze({ bytes, sha256: hash.digest('hex') });
  } finally {
    await opened.handle.close();
  }
}
