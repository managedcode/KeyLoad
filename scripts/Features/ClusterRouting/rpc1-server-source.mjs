import { createHash } from 'node:crypto';
import { chmod, mkdir, mkdtemp, open, rm, unlink } from 'node:fs/promises';
import path from 'node:path';
import { createAndInspectGitArchive, extractVerifiedArchive, runPriorCommand } from '../StorageRecovery/native5-server-archive.mjs';
import { hashInventory, inventoryPriorSource } from '../StorageRecovery/native5-server-source.mjs';

const revision = '377886f35928866f083806062b446056d64539e3';
const treeSha = '937b2c0d576ef29afb33e943ddd452722ee6a16b';
const sourceInventorySha256 = '70e92d99a96ed508ab4a1e617a5884d95cf3bfc97e99f3e131748a99cd6b640f';
const expandedBytes = 25738907;
const fileCount = 3016;
const archiveBytes = 28180480;
const archiveSha256 = 'f4a36d2febcae6e35e857c735cbebe55d41ecd9dc2652b22a67b5f541039807f';
const archiveName = 'rpc1-epoch6-server-source.tar';
const inventoryName = 'rpc1-epoch6-server-source-inventory.json';
const maximumTreeBytes = 1024 * 1024;
const maximumFiles = 100000;
const maximumPathBytes = 4096;
const maximumDepth = 64;
const invalidSource = 'The immutable RPC1 server source export is invalid.';

export const rpc1ServerSource = Object.freeze({ revision, treeSha, sourceInventorySha256, expandedBytes, fileCount,
  archiveBytes, archiveSha256, archiveName, inventoryName, dataEpoch: 6, requestInterfaceVersion: 1, peerEnvelopeVersion: 2 });

export async function createRpc1ServerSource(context) {
  const ownedRoot = await mkdtemp(path.join(context.runnerTemp, `keyload-rpc1-server-${context.runId}-${context.runAttempt}-`));
  const sourceRoot = path.join(ownedRoot, 'source');
  const archivePath = path.join(context.evidenceDirectory, archiveName);
  const inventoryPath = path.join(context.evidenceDirectory, inventoryName);
  let archiveOwned = false;
  let inventoryOwned = false;
  let primary;
  try {
    await chmod(ownedRoot, 0o700);
    await mkdir(sourceRoot, { mode: 0o700 });
    await ensureCommit(context.workspace);
    const entries = await readTree(context.workspace);
    const tree = await runPriorCommand('git', ['rev-parse', `${revision}^{tree}`], context.workspace, 256);
    if (!tree.success || decodeUtf8(tree.stdout).trim() !== treeSha) throw new Error(invalidSource);
    const archive = await createAndInspectGitArchive(context.workspace, archivePath, revision, entries);
    archiveOwned = true;
    if (archive.archiveBytes !== archiveBytes || archive.archiveSha256 !== archiveSha256) throw new Error(invalidSource);
    await extractVerifiedArchive(archivePath, sourceRoot, context.workspace);
    const inventory = await inventoryPriorSource(sourceRoot, entries);
    if (inventory.fileCount !== fileCount || inventory.expandedBytes !== expandedBytes
      || inventory.sourceInventorySha256 !== sourceInventorySha256 || hashInventory(inventory.files) !== sourceInventorySha256
      || archive.expandedBytes !== inventory.expandedBytes || !sameInventory(archive.files, inventory.files)) {
      throw new Error(invalidSource);
    }
    const inventoryBytes = Buffer.from(`${JSON.stringify({ schemaVersion: 1, files: inventory.files }, null, 2)}\n`, 'utf8');
    if (inventoryBytes.length === 0 || inventoryBytes.length > 1024 * 1024) throw new Error(invalidSource);
    const inventoryHandle = await open(inventoryPath, 'wx', 0o600);
    inventoryOwned = true;
    try { await inventoryHandle.writeFile(inventoryBytes); await inventoryHandle.sync(); }
    finally { await inventoryHandle.close(); }
    return Object.freeze({ workspace: sourceRoot, ownedRoot, archivePath, inventoryPath, inventory,
      gitEntries: entries, archiveBytes, archiveSha256, inventoryFileSha256: sha256(inventoryBytes) });
  } catch (error) {
    primary = error;
    throw error;
  } finally {
    if (primary && primary.preserveOwnedPaths !== true) {
      const failures = [];
      if (primary) {
        if (inventoryOwned) await unlink(inventoryPath).catch(failures.push.bind(failures));
        if (archiveOwned) await unlink(archivePath).catch(failures.push.bind(failures));
      }
      await rm(ownedRoot, { recursive: true, force: false }).catch(failures.push.bind(failures));
      if (failures.length > 0) throw new AggregateError(primary ? [primary, ...failures] : failures, 'RPC1 source export cleanup failed.');
    }
  }
}

export async function cleanupRpc1ServerSource(source) {
  if (source && typeof source.ownedRoot === 'string') await rm(source.ownedRoot, { recursive: true, force: false });
}

export async function verifyRpc1ServerExport(source) {
  if (!source || typeof source.workspace !== 'string' || !(source.gitEntries instanceof Map)) throw new Error(invalidSource);
  const current = await inventoryPriorSource(source.workspace, source.gitEntries);
  if (current.fileCount !== fileCount || current.expandedBytes !== expandedBytes
    || current.sourceInventorySha256 !== sourceInventorySha256 || !sameInventory(current.files, source.inventory.files)) {
    throw new Error(invalidSource);
  }
  return current;
}

export function hashRpc1Inventory(files) { return hashInventory(files); }

async function ensureCommit(workspace) {
  const probe = await runPriorCommand('git', ['cat-file', '-e', `${revision}^{commit}`], workspace, 256);
  if (probe.success) return;
  if (probe.timedOut || probe.outputLimitExceeded || probe.code !== 128
    || decodeUtf8(probe.stderr) !== `fatal: Not a valid object name ${revision}^{commit}\n`) throw new Error(invalidSource);
  const fetched = await runPriorCommand('git', ['fetch', '--no-tags', 'origin', revision], workspace, 256);
  if (!fetched.success || fetched.timedOut || fetched.outputLimitExceeded) throw new Error(invalidSource);
  const verified = await runPriorCommand('git', ['cat-file', '-e', `${revision}^{commit}`], workspace, 256);
  if (!verified.success) throw new Error(invalidSource);
}

async function readTree(workspace) {
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
    if (separator < 0 || !match || mode === 0 || !name || entries.has(name) || !Number.isSafeInteger(bytes) || bytes < 0
      || Buffer.byteLength(name, 'utf8') > maximumPathBytes || name.includes('\\') || name.split('/').length > maximumDepth
      || name.split('/').some(part => part === '' || part === '.' || part === '..')) throw new Error(invalidSource);
    entries.set(name, Object.freeze({ mode, blobSha: match[2], bytes }));
    if (entries.size > maximumFiles) throw new Error(invalidSource);
  }
  if (entries.size !== fileCount) throw new Error(invalidSource);
  return entries;
}

function sameInventory(left, right) {
  return left.length === right.length && left.every((entry, index) => entry.path === right[index].path
    && entry.mode === right[index].mode && entry.bytes === right[index].bytes && entry.sha256 === right[index].sha256);
}

function decodeUtf8(bytes) { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); }
function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
