import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, open, opendir } from 'node:fs/promises';
import path from 'node:path';
import { localImage, messages } from './local-image-contracts.mjs';

const expectedCopies = Object.freeze([
  Object.freeze({ sources: Object.freeze(['global.json', 'Directory.Build.props', 'Directory.Build.targets',
    'Directory.Packages.props', 'NuGet.Config', '.editorconfig', 'LICENSE']), destination: './' }),
  Object.freeze({ sources: Object.freeze(['KeyLoad.slnx']), destination: './KeyLoad.slnx' }),
  Object.freeze({ sources: Object.freeze(['tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets']),
    destination: './tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets' }),
  Object.freeze({ sources: Object.freeze(['src/']), destination: './src/' }),
]);
const expectedRuntimeCopy = 'COPY --from=build /app/publish/ ./';
const pinnedBasePattern = /^FROM (\S+@sha256:[a-f0-9]{64})(?: AS [a-z0-9_-]+)?$/;

export async function enumerateBuildInputs(root) {
  root = path.resolve(root);
  const selected = new Map();
  const scaffolds = new Map();
  const admission = { bytes: 0, entries: 0 };
  const dockerfile = await readTopLevelInput(root, 'Dockerfile', selected, admission);
  const ignore = await readTopLevelInput(root, '.dockerignore', selected, admission);
  const copies = parseCopies(dockerfile.bytes.toString('utf8'));
  const bases = parsePinnedBases(dockerfile.bytes.toString('utf8'));
  const ignored = parseIgnore(ignore.bytes.toString('utf8'));
  for (const copy of copies) {
    for (const source of copy.sources) {
      const normalized = source.endsWith('/') ? source.slice(0, -1) : source;
      await addRequiredParents(root, normalized, ignored, selected, scaffolds, admission);
      await visitSource(root, normalized, ignored, selected, admission, true);
    }
  }
  const entries = [...selected.entries()].sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
    .map(([relative, entry]) => Object.freeze({
      relative,
      absolute: path.join(root, ...relative.split('/')),
      kind: entry.kind,
      mode: entry.mode,
      size: entry.size,
      stat: entry.stat,
      controlDigest: entry.controlDigest ?? null,
    }));
  if (entries.length === 0) throw new Error(messages.invalidContext);
  return Object.freeze({ entries: Object.freeze(entries), scaffolds: Object.freeze([...scaffolds.values()]),
    bases: Object.freeze(bases), files: entries.length, bytes: admission.bytes });
}

async function readTopLevelInput(root, name, selected, admission) {
  const absolute = path.join(root, name);
  const before = await lstat(absolute);
  if (!before.isFile() || before.isSymbolicLink() || before.size > localImage.maxInputFileBytes) {
    throw new Error(messages.invalidContext);
  }
  addSelected(name, { kind: 'file', mode: before.mode & 0o777, size: before.size, stat: before,
    controlDigest: null }, selected, admission);
  const bytes = await readBoundedFile(absolute, before);
  const digest = createHash('sha256').update(bytes).digest('hex');
  selected.set(name, { kind: 'file', mode: before.mode & 0o777, size: before.size, stat: before,
    controlDigest: digest });
  return Object.freeze({ stat: before, bytes, digest });
}

async function addRequiredParents(root, relative, ignored, selected, scaffolds, admission) {
  const segments = toPosix(relative).split('/');
  let current = '';
  for (const segment of segments.slice(0, -1)) {
    current = current ? current + '/' + segment : segment;
    if (selected.has(current) || scaffolds.has(current)) continue;
    const absolute = path.join(root, ...current.split('/'));
    const stat = await lstat(absolute).catch(() => null);
    if (!stat?.isDirectory() || stat.isSymbolicLink() || isIgnored(current, ignored)) {
      throw new Error(messages.invalidContext);
    }
    addScaffold(current, absolute, stat, scaffolds, admission);
  }
}

async function visitSource(root, relative, ignored, selected, admission, required) {
  const absolute = path.join(root, relative);
  const before = await lstat(absolute).catch(() => null);
  if (!before || before.isSymbolicLink()) throw new Error(messages.invalidContext);
  if (isIgnored(relative, ignored)) {
    if (required) throw new Error(messages.invalidContext);
    return;
  }
  if (before.isDirectory()) {
    const posix = toPosix(relative);
    addSelected(posix, { kind: 'directory', mode: before.mode & 0o777, size: 0, stat: before }, selected, admission);
    const directory = await opendir(absolute);
    for await (const child of directory) await visitSource(root, path.join(relative, child.name), ignored, selected, admission, false);
    const after = await lstat(absolute);
    if (!sameStat(before, after)) throw new Error(messages.contextChanged);
    return;
  }
  if (!before.isFile() || before.size > localImage.maxInputFileBytes) throw new Error(messages.invalidContext);
  addSelected(toPosix(relative), { kind: 'file', mode: before.mode & 0o777, size: before.size, stat: before,
    controlDigest: null }, selected, admission);
}

function addSelected(relative, entry, selected, admission) {
  if (selected.has(relative)) return;
  if (admission.entries >= localImage.maxInputFiles) throw new Error(messages.invalidContext);
  const bytes = admission.bytes + (entry.kind === 'file' ? entry.size : 0);
  if (bytes > localImage.maxInputBytes) throw new Error(messages.invalidContext);
  selected.set(relative, entry);
  admission.entries++;
  admission.bytes = bytes;
}

function addScaffold(relative, absolute, stat, scaffolds, admission) {
  if (admission.entries >= localImage.maxInputFiles) throw new Error(messages.invalidContext);
  scaffolds.set(relative, Object.freeze({ relative, absolute, kind: 'directory', mode: stat.mode & 0o777, stat }));
  admission.entries++;
}

async function readBoundedFile(absolute, expected) {
  let handle;
  let bytes;
  const failures = [];
  try {
    handle = await open(absolute, constants.O_RDONLY | constants.O_NOFOLLOW);
    const opened = await handle.stat();
    if (!opened.isFile() || !sameStat(expected, opened)) throw new Error(messages.contextChanged);
    const buffer = Buffer.allocUnsafe(expected.size + 1);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    const afterHandle = await handle.stat();
    const afterPath = await lstat(absolute);
    if (bytesRead !== expected.size || !sameStat(expected, afterHandle) || !sameStat(expected, afterPath)) {
      throw new Error(messages.contextChanged);
    }
    bytes = buffer.subarray(0, bytesRead);
  } catch (error) {
    failures.push(error?.message === messages.contextChanged ? error : new Error(messages.invalidContext));
  }
  if (handle) {
    try { await handle.close(); }
    catch (error) { failures.push(error); }
  }
  if (failures.length === 1) throw failures[0];
  if (failures.length > 1) throw new AggregateError(failures, messages.invalidContext);
  return bytes;
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
    && (left.mode & 0o777) === (right.mode & 0o777) && left.mtimeMs === right.mtimeMs && left.ctimeMs === right.ctimeMs;
}

function toPosix(value) { return value.split(path.sep).join('/'); }
