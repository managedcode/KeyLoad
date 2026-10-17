import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, open, opendir } from 'node:fs/promises';
import path from 'node:path';
import { localImage, messages } from './local-image-contracts.mjs';

const expectedCopies = Object.freeze([
  Object.freeze({ sources: Object.freeze(['global.json', 'Directory.Build.props', 'Directory.Build.targets',
    'Directory.Packages.props', 'NuGet.Config', '.editorconfig', 'LICENSE']), destination: './' }),
  Object.freeze({ sources: Object.freeze(['KeyLoad.slnx']), destination: './KeyLoad.slnx' }),
  Object.freeze({ sources: Object.freeze(['build/CodeQuality/FunctionalCompilationIdentity.targets']),
    destination: './build/CodeQuality/FunctionalCompilationIdentity.targets' }),
  Object.freeze({ sources: Object.freeze(['analyzers/']), destination: './analyzers/' }),
  Object.freeze({ sources: Object.freeze(['src/']), destination: './src/' }),
]);
const sdkVersion = '10.0.401';
const runtimeBasePattern = /<ContainerBaseImage>(mcr\.microsoft\.com\/dotnet\/aspnet:[^<]+@sha256:[a-f0-9]{64})<\/ContainerBaseImage>/gu;
const ignoredBuildInputs = Object.freeze(['.git', '**/bin', '**/obj', '**/TestResults', '**/packages.lock.json', '**/.aspire',
  'artifacts', 'data', '.data', 'backups', 'BenchmarkDotNet.Artifacts', 'node_modules', '.env', '.env.*', '*.log', '*.binlog', '*.tmp', '.DS_Store']);

export async function enumerateBuildInputs(root) {
  root = path.resolve(root);
  const selected = new Map();
  const scaffolds = new Map();
  const admission = { bytes: 0, entries: 0 };
  const settings = await readTopLevelInput(root, 'Directory.Build.props', selected, admission);
  const global = await readTopLevelInput(root, 'global.json', selected, admission);
  if (JSON.parse(global.bytes.toString('utf8')).sdk?.version !== sdkVersion) throw new Error(messages.invalidContext);
  const matches = [...settings.bytes.toString('utf8').matchAll(runtimeBasePattern)];
  if (matches.length !== 1) throw new Error(messages.invalidContext);
  const bases = [matches[0][1]];
  const copies = expectedCopies;
  const ignored = ignoredBuildInputs;
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
