import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { lstat, readdir, readFile, readlink, realpath } from 'node:fs/promises';
import path from 'node:path';
import { parseBytes } from './aggregate-json.mjs';
import { allowedRuntimePath, requireRuntime, runtimeBundle, validateRuntimeInventory } from './compiled-test-bundle-contract.mjs';

export async function hashRuntimeFile(target, maximum = runtimeBundle.maximumFileBytes) {
  const before = await lstat(target);
  requireRuntime(before.isFile() && !before.isSymbolicLink() && before.size <= maximum);
  const hash = createHash('sha256');
  let bytes = 0;
  for await (const chunk of createReadStream(target, { highWaterMark: 1048576 })) {
    bytes += chunk.length;
    requireRuntime(bytes <= maximum);
    hash.update(chunk);
  }
  const after = await lstat(target);
  requireRuntime(before.dev === after.dev && before.ino === after.ino && before.size === after.size
    && before.mtimeMs === after.mtimeMs && before.ctimeMs === after.ctimeMs && bytes === after.size);
  return { bytes, sha256: hash.digest('hex'), mode: after.mode & 0o777 };
}

async function omitNativeAspireBundleLink(root, target, aspireVersion) {
  const relative = path.relative(root, target).split(path.sep).join('/');
  const matched = /^artifacts\/benchmark-runtime\/packages\/aspire\.cli\.(linux-(?:x64|arm64))\/(\d+\.\d+\.\d+)\/tools\/net10\.0\/\1\/bundle$/u.exec(relative);
  requireRuntime(matched !== null && matched[2] === aspireVersion, 'UnexpectedRuntimeSymbolicLink');
  const destination = path.resolve(path.dirname(target), await readlink(target));
  const version = path.basename(destination);
  requireRuntime(destination === path.join(path.dirname(target), 'versions', version)
    && version.startsWith(aspireVersion + '_') && await realpath(target) === destination,
    'UnexpectedNativeAspireBundleLink');
  const metadata = await lstat(destination);
  requireRuntime(metadata.isDirectory() && !metadata.isSymbolicLink(), 'UnexpectedNativeAspireBundleLink');
}

async function walkRuntimeDirectory(root, directory, entries, counter, aspireVersion) {
  const before = await lstat(directory);
  requireRuntime(before.isDirectory() && !before.isSymbolicLink());
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const target = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) {
      await omitNativeAspireBundleLink(root, target, aspireVersion);
      continue;
    }
    if (entry.isDirectory()) await walkRuntimeDirectory(root, target, entries, counter, aspireVersion);
    else {
      requireRuntime(entry.isFile() && entries.length < runtimeBundle.maximumFiles);
      const relative = path.relative(root, target).split(path.sep).join('/');
      requireRuntime(allowedRuntimePath(relative));
      const record = await hashRuntimeFile(target);
      counter.bytes += record.bytes;
      requireRuntime(counter.bytes <= runtimeBundle.maximumTotalBytes);
      entries.push({ path: relative, ...record });
    }
  }
}

export async function collectRuntimeFiles(root, projects, aspireVersion) {
  const entries = [];
  const counter = { bytes: 0 };
  const directories = new Set([runtimeBundle.packages]);
  for (const project of projects) {
    requireRuntime(/^(?:src|tests|benchmarks)\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+\.csproj$/u.test(project));
    if (project.startsWith('tests/') && project !== 'tests/KeyLoad.ComparisonTests/KeyLoad.ComparisonTests.csproj') continue;
    directories.add(path.posix.dirname(project) + '/bin/Release');
    directories.add(path.posix.dirname(project) + '/obj');
  }
  for (const relative of directories) {
    const directory = path.join(root, relative);
    const present = await lstat(directory).catch(error => {
      if (error.code === 'ENOENT') return null;
      throw error;
    });
    if (present !== null) await walkRuntimeDirectory(root, directory, entries, counter, aspireVersion);
  }
  entries.sort((left, right) => left.path < right.path ? -1 : left.path > right.path ? 1 : 0);
  requireRuntime(entries.some(entry => entry.path.startsWith(runtimeBundle.packages + '/')));
  return validateRuntimeInventory(entries);
}

export async function readRuntimeJson(target, maximum) {
  const metadata = await lstat(target);
  requireRuntime(metadata.isFile() && !metadata.isSymbolicLink() && metadata.size > 0 && metadata.size <= maximum);
  return parseBytes(await readFile(target));
}

export async function requireRuntimeDestination(root, entries) {
  const checked = new Set([root]);
  for (const entry of entries) {
    const target = path.join(root, entry.path);
    let directory = path.dirname(target);
    while (!checked.has(directory)) {
      requireRuntime(directory.startsWith(root + path.sep));
      const metadata = await lstat(directory).catch(error => {
        if (error.code === 'ENOENT') return null;
        throw error;
      });
      requireRuntime(metadata === null || metadata.isDirectory() && !metadata.isSymbolicLink());
      checked.add(directory);
      directory = path.dirname(directory);
    }
    const present = await lstat(target).catch(error => {
      if (error.code === 'ENOENT') return null;
      throw error;
    });
    requireRuntime(present === null);
  }
}
