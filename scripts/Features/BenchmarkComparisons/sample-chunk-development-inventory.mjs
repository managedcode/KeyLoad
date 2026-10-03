import { createHash } from 'node:crypto';
import { constants as fsConstants } from 'node:fs';
import { lstat, mkdir, open, readdir, realpath } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
const MAX_FILES = 10_000;
const MAX_FILE_BYTES = 64 * 1024 * 1024;
const MAX_TOTAL_BYTES = 512 * 1024 * 1024;

export const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
export const benchmarkDirectory = path.join(repositoryRoot, 'benchmarks/KeyLoad.Benchmarks/bin/Release/net10.0');
export const benchmarkAssembly = path.join(benchmarkDirectory, 'KeyLoad.Benchmarks.dll');

export function requireSafeRelativePath(value) {
  if (typeof value !== 'string' || value.length === 0 || value.includes('\0')) {
    throw new Error('The output path must be a nonempty path inside the repository.');
  }
  const pathBody = path.isAbsolute(value) ? value.slice(path.parse(value).root.length) : value;
  const parts = pathBody.split(/[\\/]+/);
  if (parts.some((part) => part === '..' || part === '.' || part.length === 0)) {
    throw new Error('The output path cannot contain traversal or empty path segments.');
  }
  if (!path.isAbsolute(value)) return parts.join(path.sep);
  const relative = path.relative(repositoryRoot, value);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error('The absolute output path must remain inside the repository.');
  }
  return relative;
}

export async function createOwnedOutputDirectory(argument) {
  const relative = requireSafeRelativePath(argument);
  const output = path.resolve(repositoryRoot, relative);
  const parent = path.join(repositoryRoot, 'artifacts', 'qualification');
  const name = path.basename(output);
  const allowedPrefix = name.startsWith('sample-chunk-development-') ? 'sample-chunk-development-'
    : name.startsWith('sample-chunk-dry-') ? 'sample-chunk-dry-' : null;
  if (path.dirname(output) !== parent || allowedPrefix === null || name.length < allowedPrefix.length + 8) {
    throw new Error('Output must be a new sample-chunk-development-* or sample-chunk-dry-* directory under artifacts/qualification.');
  }
  const rootInfo = await lstat(repositoryRoot);
  if (!rootInfo.isDirectory() || rootInfo.isSymbolicLink()) throw new Error('The repository root is not a plain directory.');
  await ensureArtifactParent();
  const ignored = await runGit(['check-ignore', '--no-index', '-q', '--', relative]);
  if (!ignored) {
    throw new Error('The requested output directory must be git-ignored so it cannot enter the source inventory.');
  }
  try {
    await mkdir(output, { mode: 0o700 });
  } catch (error) {
    if (error?.code === 'EEXIST') {
      throw new Error('The requested output directory already exists; existing data is never reused or removed.');
    }
    throw error;
  }
  const info = await lstat(output);
  if (!info.isDirectory() || info.isSymbolicLink()) {
    throw new Error('The newly created output is not a plain owned directory.');
  }
  return output;
}

export async function captureSourceInventory() {
  const head = (await runGitOutput(['rev-parse', 'HEAD'])).trim();
  if (!/^[0-9a-f]{40}$/i.test(head)) {
    throw new Error('Git did not provide a full source HEAD.');
  }
  const listing = await runGitBuffer(['ls-files', '--cached', '--others', '--exclude-standard', '-z']);
  const names = parseGitPaths(listing);
  const files = await measureFiles(names, repositoryRoot);
  return snapshot('source', head.toLowerCase(), files);
}

export async function captureReleaseInventory() {
  await verifyDirectoryChain(repositoryRoot, benchmarkDirectory);
  const { names, excludedGeneratedDirectories } = await enumerateReleaseFiles();
  if (!names.includes('KeyLoad.Benchmarks.dll') || !names.includes('KeyLoad.BenchmarkScenarios.dll')
      || !names.includes('KeyLoad.Core.dll') || !names.some((name) => /^Orleans\..+\.dll$/i.test(path.basename(name)))) {
    throw new Error('The Release output is missing the benchmark, scenario, Core, or native Orleans assembly.');
  }
  return { ...snapshot('release-binaries', null, await measureFiles(names, benchmarkDirectory)),
    scope: 'runtime-output-files-including-locale-and-rid-assets', excludedGeneratedDirectories,
    exclusionReason: 'BenchmarkDotNet-generated consumer build trees are ephemeral execution outputs, not the original Release dependency inventory.' };
}

export async function assertSnapshotsUnchanged(before, after, label) {
  if (before.kind !== after.kind || before.head !== after.head || before.fileCount !== after.fileCount
      || before.totalBytes !== after.totalBytes || before.sha256 !== after.sha256) {
    throw new Error(`${label} changed while the sample-chunk child was running.`);
  }
}

export async function writeOwnedJson(directory, name, value) {
  if (!/^[a-z0-9][a-z0-9.-]*\.json$/i.test(name)) {
    throw new Error('Owned JSON output name is invalid.');
  }
  const destination = path.join(directory, name);
  const bytes = Buffer.from(`${JSON.stringify(value, null, 2)}\n`, 'utf8');
  const handle = await open(destination, fsConstants.O_CREAT | fsConstants.O_EXCL | fsConstants.O_WRONLY | noFollowFlag(), 0o600);
  try {
    await handle.writeFile(bytes);
    await handle.sync();
  } finally {
    await handle.close();
  }
  return { path: name, bytes: bytes.length, sha256: sha256(bytes) };
}

export async function captureOutputFiles(directory, matching) {
  const names = await enumerateRegularFiles(directory);
  const selected = names.filter((name) => matching(name));
  return measureFiles(selected, directory);
}

export async function readBoundedRegularFile(root, relativePath, maximumBytes = MAX_FILE_BYTES) {
  const segments = relativePath.split('/');
  if (!relativePath || path.isAbsolute(relativePath) || segments.some((part) => !part || part === '..' || part === '.')) {
    throw new Error(`Invalid output file path: ${relativePath}`);
  }
  const absolute = path.join(root, ...segments);
  await verifyDirectoryChain(root, path.dirname(absolute));
  const info = await lstat(absolute);
  if (!info.isFile() || info.isSymbolicLink() || info.size > maximumBytes) {
    throw new Error(`Output is not a bounded regular file: ${relativePath}`);
  }
  const handle = await open(absolute, fsConstants.O_RDONLY | noFollowFlag());
  try {
    const opened = await handle.stat();
    if (!opened.isFile() || opened.dev !== info.dev || opened.ino !== info.ino || opened.size !== info.size) {
      throw new Error(`Output identity changed while opening: ${relativePath}`);
    }
    const bytes = await handle.readFile();
    const after = await handle.stat();
    if (bytes.length !== info.size || after.size !== info.size || after.mtimeMs !== info.mtimeMs) {
      throw new Error(`Output changed while reading: ${relativePath}`);
    }
    return bytes;
  } finally {
    await handle.close();
  }
}

export async function ensurePlainDirectory(directory) {
  const info = await lstat(directory);
  if (!info.isDirectory() || info.isSymbolicLink()) {
    throw new Error(`Expected a plain directory: ${directory}`);
  }
}

function noFollowFlag() {
  return fsConstants.O_NOFOLLOW ?? 0;
}

async function verifyDirectoryChain(root, destination) {
  const rootReal = await realpath(root);
  let current = rootReal;
  const relative = path.relative(rootReal, destination);
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error('A path escaped the repository root.');
  }
  for (const component of relative.split(path.sep).filter(Boolean)) {
    current = path.join(current, component);
    const info = await lstat(current);
    if (!info.isDirectory() || info.isSymbolicLink()) {
      throw new Error(`A path component is not a plain directory: ${current}`);
    }
  }
}

async function runGit(args) {
  try {
    await execFileAsync('git', args, { cwd: repositoryRoot, encoding: 'utf8', maxBuffer: 1024 * 1024,
      timeout: 60_000, killSignal: 'SIGTERM', windowsHide: true });
    return true;
  } catch (error) {
    if (args[0] === 'check-ignore' && error?.code === 1) {
      return false;
    }
    throw new Error(`Git command failed: git ${args.join(' ')}`, { cause: error });
  }
}

async function runGitOutput(args) {
  const { stdout } = await execFileAsync('git', args, { cwd: repositoryRoot, encoding: 'utf8', maxBuffer: 1024 * 1024,
    timeout: 60_000, killSignal: 'SIGTERM', windowsHide: true });
  return stdout;
}

async function runGitBuffer(args) {
  const { stdout } = await execFileAsync('git', args, { cwd: repositoryRoot, encoding: 'buffer', maxBuffer: 16 * 1024 * 1024,
    timeout: 60_000, killSignal: 'SIGTERM', windowsHide: true });
  return stdout;
}

async function ensureArtifactParent() {
  let current = repositoryRoot;
  for (const component of ['artifacts', 'qualification']) {
    current = path.join(current, component);
    try {
      const info = await lstat(current);
      if (!info.isDirectory() || info.isSymbolicLink()) throw new Error(`Unsafe artifact directory: ${current}`);
    } catch (error) {
      if (error?.code !== 'ENOENT') throw error;
      try {
        await mkdir(current, { mode: 0o700 });
      } catch (createError) {
        if (createError?.code !== 'EEXIST') throw createError;
      }
      const created = await lstat(current);
      if (!created.isDirectory() || created.isSymbolicLink()) throw new Error(`Unsafe artifact directory: ${current}`);
    }
  }
}

function parseGitPaths(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length === 0 || buffer.at(-1) !== 0) {
    throw new Error('Git returned an incomplete NUL-delimited source inventory.');
  }
  const names = buffer.toString('utf8').split('\0').slice(0, -1);
  if (names.length > MAX_FILES || names.some((name) => !name || name.includes('\ufffd') || path.isAbsolute(name)
      || name.split('/').some((part) => part === '..' || part === '.'))) {
    throw new Error('The source inventory has an invalid path or exceeds the file bound.');
  }
  return [...new Set(names)].sort((left, right) => left.localeCompare(right, 'en'));
}

async function enumerateRegularFiles(root) {
  await ensurePlainDirectory(root);
  const results = [];
  async function visit(directory, relative) {
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((left, right) => left.name.localeCompare(right.name, 'en'));
    for (const entry of entries) {
      const child = path.join(directory, entry.name);
      const childRelative = relative ? `${relative}/${entry.name}` : entry.name;
      const info = await lstat(child);
      if (info.isSymbolicLink()) {
        throw new Error(`A symlink is not allowed in an inventory: ${childRelative}`);
      }
      if (info.isDirectory()) {
        await visit(child, childRelative);
      } else if (info.isFile()) {
        results.push(childRelative);
        assertFileCount(results.length);
      } else {
        throw new Error(`A nonregular filesystem entry is not allowed: ${childRelative}`);
      }
    }
  }
  await visit(root, '');
  return results;
}

async function enumerateReleaseFiles() {
  const names = [];
  const excludedGeneratedDirectories = [];
  async function visit(directory, relative) {
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((left, right) => left.name.localeCompare(right.name, 'en'));
    for (const entry of entries) {
      const childRelative = relative ? `${relative}/${entry.name}` : entry.name;
      const child = path.join(directory, entry.name);
      const info = await lstat(child);
      if (info.isSymbolicLink()) throw new Error(`A symlink is not allowed in Release dependencies: ${childRelative}`);
      if (info.isDirectory() && /^KeyLoad\.BenchmarkScenarios-.+-[0-9]+$/.test(entry.name)) {
        excludedGeneratedDirectories.push(childRelative);
      } else if (info.isDirectory()) {
        await visit(child, childRelative);
      } else if (info.isFile()) {
        names.push(childRelative);
        assertFileCount(names.length);
      } else {
        throw new Error(`A nonregular Release entry is not allowed: ${childRelative}`);
      }
    }
  }
  await visit(benchmarkDirectory, '');
  names.sort((left, right) => left.localeCompare(right, 'en'));
  excludedGeneratedDirectories.sort((left, right) => left.localeCompare(right, 'en'));
  return { names, excludedGeneratedDirectories };
}

async function measureFiles(names, root) {
  assertFileCount(names.length);
  let totalBytes = 0;
  const files = [];
  for (const name of names) {
    const segments = name.split(/[\\/]/);
    if (!name || path.isAbsolute(name) || segments.some((part) => part === '..' || part === '.')) {
      throw new Error(`Invalid file path in inventory: ${name}`);
    }
    const absolute = path.join(root, ...segments);
    await verifyDirectoryChain(root, path.dirname(absolute));
    const file = await hashRegularFile(absolute, name);
    totalBytes += file.bytes;
    if (file.bytes > MAX_FILE_BYTES || totalBytes > MAX_TOTAL_BYTES) {
      throw new Error('The file inventory exceeds its per-file or total byte bound.');
    }
    files.push(file);
  }
  return files;
}

async function hashRegularFile(filePath, relativePath) {
  const before = await lstat(filePath);
  if (!before.isFile() || before.isSymbolicLink() || before.size > MAX_FILE_BYTES) {
    throw new Error(`Inventory entries must be bounded regular files: ${relativePath}`);
  }
  const handle = await open(filePath, fsConstants.O_RDONLY | noFollowFlag());
  try {
    const opened = await handle.stat();
    if (!opened.isFile() || opened.dev !== before.dev || opened.ino !== before.ino || opened.size !== before.size) {
      throw new Error(`File identity changed while opening: ${relativePath}`);
    }
    const hash = createHash('sha256');
    const buffer = Buffer.allocUnsafe(64 * 1024);
    let bytes = 0;
    while (true) {
      const { bytesRead } = await handle.read(buffer, 0, buffer.length, null);
      if (bytesRead === 0) break;
      bytes += bytesRead;
      if (bytes > MAX_FILE_BYTES) throw new Error(`File exceeds its inventory bound: ${relativePath}`);
      hash.update(buffer.subarray(0, bytesRead));
    }
    const after = await handle.stat();
    if (bytes !== before.size || after.size !== before.size || after.mtimeMs !== before.mtimeMs) {
      throw new Error(`File changed while hashing: ${relativePath}`);
    }
    return { path: relativePath.replaceAll(path.sep, '/'), bytes, sha256: hash.digest('hex') };
  } finally {
    await handle.close();
  }
}

function snapshot(kind, head, files) {
  const totalBytes = files.reduce((sum, file) => sum + file.bytes, 0);
  const digestInput = JSON.stringify(files.map(({ path: name, bytes, sha256 }) => [name, bytes, sha256]));
  return { kind, head, fileCount: files.length, totalBytes, sha256: sha256(Buffer.from(digestInput)), files };
}

function assertFileCount(count) {
  if (count > MAX_FILES) throw new Error('The file inventory exceeds its 10,000-file bound.');
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}
