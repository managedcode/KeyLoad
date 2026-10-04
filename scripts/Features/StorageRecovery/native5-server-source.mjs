import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { chmod, lstat, mkdir, mkdtemp, open, opendir, rm } from 'node:fs/promises';
import path from 'node:path';
import { createAndInspectGitArchive, extractVerifiedArchive, runPriorCommand } from './native5-server-archive.mjs';

const fixedRevision = '7784b6b46b98ce994dd98070dc1f58fe4e506b91';
const fixedTree = 'b03bf1301a03b3fe00f419c3c7bf5285a34b63f9';
const fixedInventorySha256 = '2d60112c44b55fdbcfd36bdeb14bf821e38d55e43df09c0c85f10558a5e332d0';
const fixedExpandedBytes = 22520638;
const fixedFileCount = 2557;
const maximumArchiveBytes = 4 * 1024 * 1024 * 1024;
const maximumFiles = 100000;
const maximumDepth = 64;
const maximumPathBytes = 4096;
const maximumPathTotalBytes = 16 * 1024 * 1024;
const maximumTreeBytes = 1024 * 1024;
const chunkBytes = 64 * 1024;
const archiveName = 'prior-server-source.tar';
const inventoryName = 'prior-server-source-inventory.json';
const invalidSource = 'The immutable native5 source export is invalid.';

export const native5ServerSource = Object.freeze({
  revision: fixedRevision,
  treeSha: fixedTree,
  sourceInventorySha256: fixedInventorySha256,
  expandedBytes: fixedExpandedBytes,
  fileCount: fixedFileCount,
  archiveName,
  inventoryName,
});

export async function createNative5ServerSource(context) {
  const temporaryRoot = path.join(context.runnerTemp, `keyload-native5-server-${context.runId}-${context.runAttempt}-`);
  const ownedRoot = await mkdtemp(temporaryRoot);
  const exportRoot = path.join(ownedRoot, 'source');
  const archivePath = path.join(context.evidenceDirectory, archiveName);
  let primary;
  try {
    await chmod(ownedRoot, 0o700);
    await mkdir(exportRoot, { mode: 0o700 });
    await ensurePriorCommit(context.workspace);
    const entries = await readPriorTree(context.workspace);
    const treeResult = await runPriorCommand('git', ['rev-parse', `${fixedRevision}^{tree}`], context.workspace, 128);
    const treeSha = decodeUtf8(treeResult.stdout).trim();
    if (!treeResult.success) throw new Error(invalidSource);
    if (treeSha.trim() !== fixedTree) throw new Error(invalidSource);
    const archive = await createAndInspectGitArchive(context.workspace, archivePath, fixedRevision, entries);
    await extractVerifiedArchive(archivePath, exportRoot, context.workspace);
    const inventory = await inventoryPriorSource(exportRoot, entries);
    if (inventory.fileCount !== fixedFileCount || inventory.expandedBytes !== fixedExpandedBytes
      || inventory.sourceInventorySha256 !== fixedInventorySha256 || hashInventory(archive.files) !== inventory.sourceInventorySha256
      || archive.expandedBytes !== inventory.expandedBytes || !sameInventory(archive.files, inventory.files)) throw new Error(invalidSource);
    const inventoryBytes = Buffer.from(`${JSON.stringify({ schemaVersion: 1, files: inventory.files }, null, 2)}\n`, 'utf8');
    if (inventoryBytes.length > 1024 * 1024) throw new Error(invalidSource);
    await writeExclusiveEvidence(context.evidenceDirectory, inventoryName, inventoryBytes);
    return Object.freeze({ workspace: exportRoot, archivePath, archiveSha256: archive.archiveSha256,
      archiveBytes: archive.archiveBytes, inventory, gitEntries: entries, ownedRoot });
  } catch (error) {
    primary = error;
    throw error;
  } finally {
    if (primary && primary.preserveOwnedPaths !== true) await removeOwnedDirectory(ownedRoot, primary);
  }
}

export async function cleanupNative5ServerSource(source) {
  if (source && typeof source.ownedRoot === 'string') await rm(source.ownedRoot, { recursive: true, force: false });
}

export async function inventoryPriorSource(root, gitEntries) {
  const files = [];
  const expectedDirectories = new Set();
  for (const file of gitEntries.keys()) {
    const segments = file.split('/');
    for (let index = 1; index < segments.length; index++) expectedDirectories.add(segments.slice(0, index).join('/'));
  }
  let expandedBytes = 0;
  let pathTotalBytes = 0;
  const pending = [''];
  while (pending.length > 0) {
    const relative = pending.pop();
    const directory = path.join(root, relative);
    const directoryInfo = await lstat(directory);
    if (!directoryInfo.isDirectory() || directoryInfo.isSymbolicLink()) throw new Error(invalidSource);
    const children = await opendir(directory, { encoding: 'buffer', bufferSize: 32 });
    for await (const child of children) {
      const childName = decodeUtf8(child.name);
      const childPath = relative.length === 0 ? childName : `${relative}/${childName}`;
      const pathBytes = Buffer.from(childPath, 'utf8');
      if (path.isAbsolute(childPath) || childPath.includes('\\') || childPath.split('/').some(part => part === '' || part === '.' || part === '..')
        || pathBytes.length > maximumPathBytes || childPath.split('/').length > maximumDepth) throw new Error(invalidSource);
      pathTotalBytes += pathBytes.length;
      if (pathTotalBytes > maximumPathTotalBytes) throw new Error(invalidSource);
      const absolute = path.join(root, childPath);
      const info = await lstat(absolute);
      if (info.isSymbolicLink()) throw new Error(invalidSource);
      if (info.isDirectory()) {
        if (!expectedDirectories.has(childPath)) throw new Error(invalidSource);
        pending.push(childPath);
        continue;
      }
      const mode = info.mode & 0o7777;
      if (!info.isFile() || mode !== 0o644 && mode !== 0o755) throw new Error(invalidSource);
      if (files.length >= maximumFiles) throw new Error(invalidSource);
      expandedBytes += info.size;
      if (expandedBytes > maximumArchiveBytes) throw new Error(invalidSource);
      const digest = await hashFile(absolute, info);
      const gitEntry = gitEntries.get(childPath);
      if (!gitEntry || gitEntry.mode !== mode || gitEntry.bytes !== info.size || digest.gitBlobSha !== gitEntry.blobSha) {
        throw new Error(invalidSource);
      }
      files.push(Object.freeze({ path: childPath, mode, bytes: info.size, sha256: digest.sha256 }));
    }
  }
  files.sort((left, right) => Buffer.compare(Buffer.from(left.path, 'utf8'), Buffer.from(right.path, 'utf8')));
  const sourceInventorySha256 = hashInventory(files);
  return Object.freeze({ files: Object.freeze(files), fileCount: files.length, expandedBytes, sourceInventorySha256 });
}

export async function verifyNative5ServerExport(source) {
  if (!source || typeof source.workspace !== 'string' || !(source.gitEntries instanceof Map)) throw new Error(invalidSource);
  const current = await inventoryPriorSource(source.workspace, source.gitEntries);
  if (current.fileCount !== source.inventory.fileCount || current.expandedBytes !== source.inventory.expandedBytes
    || current.sourceInventorySha256 !== source.inventory.sourceInventorySha256 || !sameInventory(current.files, source.inventory.files)) {
    throw new Error(invalidSource);
  }
  return current;
}

export function hashInventory(files) {
  const hash = createHash('sha256');
  for (const file of files) {
    const name = Buffer.from(file.path, 'utf8');
    const row = Buffer.allocUnsafe(4 + name.length + 4 + 8 + 32);
    row.writeUInt32LE(name.length, 0);
    name.copy(row, 4);
    row.writeUInt32LE(file.mode, 4 + name.length);
    row.writeBigUInt64LE(BigInt(file.bytes), 8 + name.length);
    Buffer.from(file.sha256, 'hex').copy(row, 16 + name.length);
    hash.update(row);
  }
  return hash.digest('hex');
}

async function ensurePriorCommit(workspace) {
  const probe = await runPriorCommand('git', ['cat-file', '-e', `${fixedRevision}^{commit}`], workspace, 128);
  if (probe.success) return;
  if (probe.timedOut || probe.outputLimitExceeded || probe.code !== 128
    || decodeUtf8(probe.stderr) !== `fatal: Not a valid object name ${fixedRevision}^{commit}\n`) throw new Error(invalidSource);
  const fetched = await runPriorCommand('git', ['fetch', '--no-tags', 'origin', fixedRevision], workspace, 128);
  if (!fetched.success || fetched.timedOut || fetched.outputLimitExceeded) throw new Error(invalidSource);
  const verified = await runPriorCommand('git', ['cat-file', '-e', `${fixedRevision}^{commit}`], workspace, 128);
  if (!verified.success) throw new Error(invalidSource);
}

async function readPriorTree(workspace) {
  const result = await runPriorCommand('git', ['ls-tree', '-rlz', '--full-tree', fixedRevision], workspace, maximumTreeBytes);
  if (!result.success) throw new Error(invalidSource);
  const entries = new Map();
  for (const record of decodeUtf8(result.stdout).split('\0')) {
    if (!record) continue;
    const separator = record.indexOf('\t');
    const header = /^(100644|100755) blob ([0-9a-f]{40}) +([0-9]+)$/.exec(record.slice(0, separator));
    const name = record.slice(separator + 1);
    const mode = header?.[1] === '100644' ? 0o644 : header?.[1] === '100755' ? 0o755 : 0;
    const blobSha = header?.[2];
    const size = Number(header?.[3]);
    const pathBytes = Buffer.from(name, 'utf8');
    if (separator < 0 || !header || mode === 0 || !/^[0-9a-f]{40}$/.test(blobSha ?? '')
      || !name || pathBytes.length > maximumPathBytes || name.includes('\\') || name.split('/').some(part => part === '' || part === '.' || part === '..')
      || name.split('/').length > maximumDepth || entries.has(name) || !Number.isSafeInteger(size) || size < 0) throw new Error(invalidSource);
    entries.set(name, Object.freeze({ mode, blobSha, bytes: size }));
    if (entries.size > maximumFiles) throw new Error(invalidSource);
  }
  if (entries.size !== fixedFileCount) throw new Error(invalidSource);
  return entries;
}

async function hashFile(file, expected) {
  const input = await open(file, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  const hash = createHash('sha256');
  const gitBlob = createHash('sha1').update(`blob ${expected.size}\0`);
  const buffer = Buffer.allocUnsafe(chunkBytes);
  let offset = 0;
  try {
    const before = await input.stat();
    if (before.dev !== expected.dev || before.ino !== expected.ino || before.size !== expected.size
      || (before.mode & 0o7777) !== (expected.mode & 0o7777) || !before.isFile()) throw new Error(invalidSource);
    while (offset < expected.size) {
      const { bytesRead } = await input.read(buffer, 0, Math.min(buffer.length, expected.size - offset), offset);
      if (bytesRead === 0) throw new Error(invalidSource);
      const content = buffer.subarray(0, bytesRead);
      hash.update(content);
      gitBlob.update(content);
      offset += bytesRead;
    }
    const after = await input.stat();
    if (after.dev !== expected.dev || after.ino !== expected.ino || after.size !== expected.size
      || (after.mode & 0o7777) !== (expected.mode & 0o7777)) throw new Error(invalidSource);
    return Object.freeze({ sha256: hash.digest('hex'), gitBlobSha: gitBlob.digest('hex') });
  } finally { await input.close(); }
}

function sameInventory(left, right) {
  return left.length === right.length && left.every((entry, index) => entry.path === right[index].path
    && entry.mode === right[index].mode && entry.bytes === right[index].bytes && entry.sha256 === right[index].sha256);
}

function decodeUtf8(bytes) { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); }

async function writeExclusiveEvidence(directory, name, bytes) {
  const target = path.join(directory, name);
  const handle = await open(target, 'wx', 0o600);
  try { await handle.writeFile(bytes); await handle.sync(); } finally { await handle.close(); }
}

async function removeOwnedDirectory(directory, primary) {
  try { await rm(directory, { recursive: true, force: false }); }
  catch (cleanup) { throw new AggregateError([primary, cleanup], 'Prior source failure and temporary-export cleanup failure.'); }
}
