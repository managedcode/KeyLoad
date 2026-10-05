import { createHash } from 'node:crypto';
import { chmod, mkdir, mkdtemp, open, rm, unlink } from 'node:fs/promises';
import path from 'node:path';
import { createAndInspectGitArchive, extractVerifiedArchive, runPriorCommand, verifyRetainedArchive } from '../StorageRecovery/native5-server-archive.mjs';
import { hashInventory, inventoryPriorSource } from '../StorageRecovery/native5-server-source.mjs';

const revision = '1e8833c027cf232e35fe012cd3eed41c61a17f89';
const archiveName = 'interface3-epoch7-server-source.tar';
const inventoryName = 'interface3-epoch7-server-source-inventory.json';
const maximumTreeBytes = 1024 * 1024;
const maximumFiles = 100000;
const maximumPathBytes = 4096;
const maximumDepth = 64;
const maximumArchiveBytes = 4 * 1024 * 1024 * 1024;
const invalidSource = 'The immutable interface3 server source export is invalid.';

export const interface3ServerSource = Object.freeze({ revision, archiveName, inventoryName,
  dataEpoch: 7, requestInterfaceAlias: 'keyload.request.v2', requestInterfaceVersion: 3, peerEnvelopeVersion: 3 });

export async function createInterface3ServerSource(context) {
  const ownedRoot = await mkdtemp(path.join(context.runnerTemp, `keyload-interface3-server-${context.runId}-${context.runAttempt}-`));
  const sourceRoot = path.join(ownedRoot, 'source');
  const archivePath = path.join(context.evidenceDirectory, archiveName);
  const inventoryPath = path.join(context.evidenceDirectory, inventoryName);
  let archiveOwned = false;
  let inventoryOwned = false;
  let primary;
  try {
    await chmod(ownedRoot, 0o700);
    await mkdir(sourceRoot, { mode: 0o700 });
    await ensurePinnedCommit(context.workspace);
    const tree = await readPinnedTree(context.workspace);
    const archive = await createAndInspectGitArchive(context.workspace, archivePath, revision, tree.entries);
    archiveOwned = true;
    if (archive.archiveBytes > maximumArchiveBytes) throw new Error(invalidSource);
    await extractVerifiedArchive(archivePath, sourceRoot, context.workspace);
    const inventory = await inventoryPriorSource(sourceRoot, tree.entries);
    if (inventory.fileCount !== tree.entries.size || archive.expandedBytes !== inventory.expandedBytes
      || hashInventory(inventory.files) !== inventory.sourceInventorySha256) throw new Error(invalidSource);
    const inventoryBytes = Buffer.from(`${JSON.stringify({ schemaVersion: 1, files: inventory.files }, null, 2)}\n`, 'utf8');
    if (inventoryBytes.length === 0 || inventoryBytes.length > 1024 * 1024) throw new Error(invalidSource);
    const handle = await open(inventoryPath, 'wx', 0o600);
    inventoryOwned = true;
    try { await handle.writeFile(inventoryBytes); await handle.sync(); }
    finally { await handle.close(); }
    return Object.freeze({ workspace: sourceRoot, repositoryWorkspace: context.workspace,
      ownedRoot, archivePath, inventoryPath, inventory,
      treeSha: tree.treeSha, archiveBytes: archive.archiveBytes, archiveSha256: archive.archiveSha256,
      inventoryFileSha256: sha256(inventoryBytes), gitEntries: tree.entries });
  } catch (error) {
    primary = error;
    throw error;
  } finally {
    if (primary && primary.preserveOwnedPaths !== true) {
      const failures = [];
      if (inventoryOwned) await unlink(inventoryPath).catch(failures.push.bind(failures));
      if (archiveOwned) await unlink(archivePath).catch(failures.push.bind(failures));
      await rm(ownedRoot, { recursive: true, force: false }).catch(failures.push.bind(failures));
      if (failures.length > 0) throw new AggregateError([primary, ...failures], 'Interface3 source export cleanup failed.');
    }
  }
}

export async function verifyInterface3ServerExport(source) {
  if (!source || typeof source.workspace !== 'string' || typeof source.repositoryWorkspace !== 'string'
    || !(source.gitEntries instanceof Map)) throw new Error(invalidSource);
  const tree = await readPinnedTree(source.repositoryWorkspace);
  if (tree.treeSha !== source.treeSha || !sameGitEntries(tree.entries, source.gitEntries)) throw new Error(invalidSource);
  const current = await inventoryPriorSource(source.workspace, tree.entries);
  if (!sameInventory(current.files, source.inventory.files)
    || current.fileCount !== source.inventory.fileCount || current.expandedBytes !== source.inventory.expandedBytes
    || current.sourceInventorySha256 !== source.inventory.sourceInventorySha256) throw new Error(invalidSource);
  return current;
}

export async function verifyRetainedInterface3Archive(context, source, rows) {
  const ownedRoot = await mkdtemp(path.join(context.runnerTemp, `keyload-interface3-verify-${context.runId}-${context.runAttempt}-`));
  let primary;
  try {
    await chmod(ownedRoot, 0o700);
    const tree = await readPinnedTree(context.workspace);
    if (tree.treeSha !== source.treeSha) throw new Error(invalidSource);
    await verifyRetainedArchive(context.evidenceDirectory, archiveName, rows, source.archiveBytes,
      source.archiveSha256, revision);
    const extracted = path.join(ownedRoot, 'source');
    await mkdir(extracted, { mode: 0o700 });
    await extractVerifiedArchive(path.join(context.evidenceDirectory, archiveName), extracted, context.workspace);
    const actual = await inventoryPriorSource(extracted, tree.entries);
    if (actual.fileCount !== source.fileCount || actual.expandedBytes !== source.expandedBytes
      || actual.sourceInventorySha256 !== source.sourceInventorySha256 || !sameInventory(actual.files, rows)) {
      throw new Error(invalidSource);
    }
    return actual;
  } catch (error) {
    primary = error;
    throw error;
  } finally {
    try { await rm(ownedRoot, { recursive: true, force: false }); }
    catch (cleanup) {
      if (primary) throw new AggregateError([primary, cleanup], 'Interface3 archive verification cleanup failed.');
      throw cleanup;
    }
  }
}

export async function cleanupInterface3ServerSource(source) {
  if (source && typeof source.ownedRoot === 'string') await rm(source.ownedRoot, { recursive: true, force: false });
}

async function ensurePinnedCommit(workspace) {
  const probe = await runPriorCommand('git', ['cat-file', '-e', `${revision}^{commit}`], workspace, 256);
  if (probe.success) return;
  if (probe.timedOut || probe.outputLimitExceeded || probe.code !== 128
    || decodeUtf8(probe.stderr) !== `fatal: Not a valid object name ${revision}^{commit}\n`) throw new Error(invalidSource);
  const fetched = await runPriorCommand('git', ['fetch', '--no-tags', 'origin', revision], workspace, 256);
  if (!fetched.success || fetched.timedOut || fetched.outputLimitExceeded) throw new Error(invalidSource);
  const verified = await runPriorCommand('git', ['cat-file', '-e', `${revision}^{commit}`], workspace, 256);
  if (!verified.success || verified.timedOut || verified.outputLimitExceeded) throw new Error(invalidSource);
}

async function readPinnedTree(workspace) {
  const treeResult = await runPriorCommand('git', ['rev-parse', `${revision}^{tree}`], workspace, 256);
  if (!treeResult.success) throw new Error(invalidSource);
  const treeSha = decodeUtf8(treeResult.stdout).trim();
  if (!/^[a-f0-9]{40}$/.test(treeSha)) throw new Error(invalidSource);
  const result = await runPriorCommand('git', ['ls-tree', '-rlz', '--full-tree', revision], workspace, maximumTreeBytes);
  if (!result.success) throw new Error(invalidSource);
  const entries = new Map();
  for (const record of decodeUtf8(result.stdout).split('\0')) {
    if (record.length === 0) continue;
    const separator = record.indexOf('\t');
    const match = /^(100644|100755) blob ([0-9a-f]{40}) +([0-9]+)$/.exec(record.slice(0, separator));
    const name = record.slice(separator + 1);
    const mode = match?.[1] === '100644' ? 0o644 : match?.[1] === '100755' ? 0o755 : 0;
    const bytes = Number(match?.[3]);
    if (separator < 0 || !match || mode === 0 || !name || entries.has(name) || !Number.isSafeInteger(bytes)
      || bytes < 0 || Buffer.byteLength(name, 'utf8') > maximumPathBytes || name.includes('\\')
      || name.split('/').length > maximumDepth || name.split('/').some(part => part === '' || part === '.' || part === '..')) {
      throw new Error(invalidSource);
    }
    entries.set(name, Object.freeze({ mode, blobSha: match[2], bytes }));
    if (entries.size > maximumFiles) throw new Error(invalidSource);
  }
  if (entries.size === 0) throw new Error(invalidSource);
  return Object.freeze({ treeSha, entries });
}

function sameGitEntries(left, right) {
  if (left.size !== right.size) return false;
  for (const [name, expected] of left) {
    const actual = right.get(name);
    if (!actual || actual.mode !== expected.mode || actual.bytes !== expected.bytes || actual.blobSha !== expected.blobSha) return false;
  }
  return true;
}

function sameInventory(left, right) {
  return Array.isArray(left) && Array.isArray(right) && left.length === right.length
    && left.every((entry, index) => entry.path === right[index].path && entry.mode === right[index].mode
      && entry.bytes === right[index].bytes && entry.sha256 === right[index].sha256);
}

function decodeUtf8(bytes) { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); }
function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
