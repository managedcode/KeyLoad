import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, open, readdir } from 'node:fs/promises';
import path from 'node:path';
import { localImage, messages } from './local-image-contracts.mjs';

const expectedCopies = Object.freeze([
  Object.freeze({ sources: Object.freeze(['global.json', 'Directory.Build.props', 'Directory.Build.targets',
    'Directory.Packages.props', 'NuGet.Config', '.editorconfig', 'LICENSE']), destination: './' }),
  Object.freeze({ sources: Object.freeze(['src/']), destination: './src/' }),
]);
const expectedRuntimeCopy = 'COPY --from=build /app/publish/ ./';
const pinnedBasePattern = /^FROM (\S+@sha256:[a-f0-9]{64})(?: AS [a-z0-9_-]+)?$/;

export async function readBuildInputs(root) {
  const dockerfileBytes = await readTopLevelFile(root, 'Dockerfile');
  const dockerfile = dockerfileBytes.toString('utf8');
  const copies = parseCopies(dockerfile);
  const bases = parsePinnedBases(dockerfile);
  const ignoreBytes = await readTopLevelFile(root, '.dockerignore');
  const ignored = parseIgnore(ignoreBytes.toString('utf8'));
  const selected = new Map();
  await addFile(root, '.dockerignore', ignored, selected);
  await addFile(root, 'Dockerfile', ignored, selected);
  for (const copy of copies) {
    for (const source of copy.sources) {
      const normalized = source.endsWith('/') ? source.slice(0, -1) : source;
      await visitSource(root, normalized, ignored, selected);
    }
  }
  const entries = [...selected.entries()].sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0);
  if (entries.length === 0 || entries.length > localImage.maxInputFiles) throw new Error(messages.invalidContext);
  const digest = createHash('sha256');
  let totalBytes = 0;
  for (const [relative, entry] of entries) {
    if (entry.kind === 'file') totalBytes += entry.bytes.length;
    if (totalBytes > localImage.maxInputBytes) throw new Error(messages.invalidContext);
    digest.update(frame(relative));
    digest.update(Buffer.from([entry.kind === 'directory' ? 1 : 2]));
    digest.update(uint32(entry.mode));
    digest.update(uint64(entry.kind === 'directory' ? 0 : entry.bytes.length));
    if (entry.bytes) digest.update(entry.bytes);
  }
  return Object.freeze({ digest: `sha256:${digest.digest('hex')}`, bases, files: entries.length, bytes: totalBytes });
}

async function readTopLevelFile(root, name) {
  const absolute = path.join(root, name);
  const before = await lstat(absolute);
  if (!before.isFile() || before.isSymbolicLink() || before.size > localImage.maxInputFileBytes) {
    throw new Error(messages.invalidContext);
  }
  return readBoundedFile(absolute, before);
}

async function visitSource(root, relative, ignored, selected) {
  const absolute = path.join(root, relative);
  const before = await lstat(absolute).catch(() => null);
  if (!before || before.isSymbolicLink()) throw new Error(messages.invalidContext);
  if (isIgnored(relative, ignored)) return;
  if (before.isDirectory()) {
    selected.set(toPosix(relative), { kind: 'directory', mode: before.mode & 0o777, bytes: null });
    const names = await readdir(absolute);
    names.sort((left, right) => left.localeCompare(right, 'en'));
    for (const name of names) await visitSource(root, path.join(relative, name), ignored, selected);
    const after = await lstat(absolute);
    if (!sameStat(before, after)) throw new Error(messages.contextChanged);
    return;
  }
  if (!before.isFile() || before.size > localImage.maxInputFileBytes) throw new Error(messages.invalidContext);
  const bytes = await readBoundedFile(absolute, before);
  selected.set(toPosix(relative), { kind: 'file', mode: before.mode & 0o777, bytes });
}

async function readBoundedFile(absolute, expected) {
  let handle;
  try {
    handle = await open(absolute, constants.O_RDONLY | constants.O_NOFOLLOW);
    const opened = await handle.stat();
    if (!opened.isFile() || opened.dev !== expected.dev || opened.ino !== expected.ino
      || opened.size !== expected.size) throw new Error(messages.contextChanged);
    const bytes = Buffer.allocUnsafe(expected.size + 1);
    const { bytesRead } = await handle.read(bytes, 0, bytes.length, 0);
    const afterHandle = await handle.stat();
    const afterPath = await lstat(absolute);
    if (bytesRead !== expected.size || !sameStat(expected, afterHandle) || !sameStat(expected, afterPath)) {
      throw new Error(messages.contextChanged);
    }
    return bytes.subarray(0, bytesRead);
  } catch (error) {
    if (error?.message === messages.contextChanged) throw error;
    throw new Error(messages.invalidContext);
  } finally {
    await handle?.close();
  }
}

async function addFile(root, relative, ignored, selected) {
  if (isIgnored(relative, ignored)) throw new Error(messages.invalidContext);
  await visitSource(root, relative, ignored, selected);
}

function parseCopies(dockerfile) {
  const copies = [];
  let runtimeCopySeen = false;
  for (const line of dockerfile.split(/\r?\n/u)) {
    if (!/^\s*COPY\b/iu.test(line)) continue;
    if (line === expectedRuntimeCopy) {
      if (runtimeCopySeen) throw new Error(messages.invalidContext);
      runtimeCopySeen = true;
      continue;
    }
    const match = /^COPY\s+(.+?)\s+([^\s]+)\s*$/u.exec(line);
    if (!match || match[1].includes('--') || match[1].includes('*')) throw new Error(messages.invalidContext);
    const sources = match[1].split(/\s+/u);
    copies.push({ sources, destination: match[2] });
  }
  if (!runtimeCopySeen || copies.length !== expectedCopies.length || copies.some((copy, index) =>
    copy.destination !== expectedCopies[index].destination
    || copy.sources.length !== expectedCopies[index].sources.length
    || copy.sources.some((source, sourceIndex) => source !== expectedCopies[index].sources[sourceIndex]))) {
    throw new Error(messages.invalidContext);
  }
  return copies;
}

function parsePinnedBases(dockerfile) {
  const bases = dockerfile.split(/\r?\n/u).filter(line => /^FROM\s/u.test(line)).map(line => {
    const match = pinnedBasePattern.exec(line);
    if (!match) throw new Error(messages.invalidContext);
    return match[1];
  });
  if (bases.length !== 2) throw new Error(messages.invalidContext);
  return bases;
}

function parseIgnore(text) {
  const rules = text.split(/\r?\n/u).filter(line => line.length > 0 && !line.startsWith('#'));
  const supported = new Set(['.git', '**/bin', '**/obj', '**/TestResults', '**/packages.lock.json', '**/.aspire', 'artifacts', 'data', '.data', 'backups',
    'BenchmarkDotNet.Artifacts', 'node_modules', '.env', '.env.*', '*.log', '*.binlog', '*.tmp', '.DS_Store']);
  if (rules.some(rule => !supported.has(rule))) throw new Error(messages.invalidContext);
  return rules;
}

function isIgnored(relative, rules) {
  const normalized = toPosix(relative);
  const parts = normalized.split('/');
  return rules.some(rule => {
    if (rule.startsWith('**/')) return parts.includes(rule.slice(3));
    if (rule.startsWith('*.')) return !normalized.includes('/') && normalized.endsWith(rule.slice(1));
    if (rule === '.env.*') return !normalized.includes('/') && parts[0].startsWith('.env.');
    return normalized === rule || normalized.startsWith(rule + '/');
  });
}

function sameStat(left, right) {
  return left.dev === right.dev && left.ino === right.ino && left.size === right.size
    && (left.mode & 0o777) === (right.mode & 0o777) && left.mtimeMs === right.mtimeMs;
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

function toPosix(value) { return value.split(path.sep).join('/'); }
