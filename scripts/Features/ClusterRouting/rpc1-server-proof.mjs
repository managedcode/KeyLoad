import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import path from 'node:path';
import { baseImage, imageBuild, imageReference, registry, registryProtocol, validation } from '../BenchmarkComparisons/image-contracts.mjs';
import { parseBytes } from '../BenchmarkComparisons/aggregate-json.mjs';
import { parseManifestEvidence } from '../BenchmarkComparisons/image-manifest.mjs';
import { verifyRetainedArchive } from '../StorageRecovery/native5-server-archive.mjs';
import { rpc1ServerSource, hashRpc1Inventory } from './rpc1-server-source.mjs';

const receiptName = 'rpc1-epoch6-server-image-receipt.json';
const manifestName = 'rpc1-epoch6-server-manifest.json';
const inventoryName = rpc1ServerSource.inventoryName;
const archiveName = rpc1ServerSource.archiveName;
const maximumReceiptBytes = 64 * 1024;
const maximumManifestBytes = 1024 * 1024;
const maximumInventoryBytes = 1024 * 1024;
const maximumArchiveBytes = 4 * 1024 * 1024 * 1024;
const maximumAnnotationEntries = 256;
const maximumAnnotationKeyBytes = 256;
const maximumAnnotationValueBytes = 4096;
const maximumLayers = 1024;
const invalidProof = 'The immutable RPC1 server image proof is invalid.';
const receiptKeys = Object.freeze(['schemaVersion', 'kind', 'producer', 'imageSource', 'protocolProfile', 'bases', 'image']);
const producerKeys = Object.freeze(['sourceSha', 'runId', 'runAttempt', 'repository', 'ref', 'workflow', 'job']);
const sourceKeys = Object.freeze(['revision', 'treeSha', 'archiveFile', 'archiveSha256', 'archiveBytes', 'inventoryFile',
  'inventoryFileSha256', 'sourceInventorySha256', 'fileCount', 'expandedBytes', 'overlayCount']);
const baseKeys = Object.freeze(['sdk', 'runtime', 'registry']);
const imageKeys = Object.freeze(['name', 'taggedReference', 'reference', 'manifestFile', 'manifestSha256', 'registryDigest',
  'contentType', 'configId', 'revisionLabel']);
const inventoryKeys = Object.freeze(['schemaVersion', 'files']);
const inventoryRowKeys = Object.freeze(['path', 'mode', 'bytes', 'sha256']);
const sha256Pattern = /^[a-f0-9]{64}$/;
const digestPattern = /^sha256:[a-f0-9]{64}$/;
const imageTagPattern = /^127\.0\.0\.1:5000\/keyload\/server:[a-f0-9]{40}-[1-9][0-9]{0,19}-[1-9][0-9]{0,19}$/;
const profileKeys = Object.freeze(['dataEpoch', 'requestInterfaceAlias', 'requestInterfaceVersion', 'peerEnvelopeVersion']);
const manifestTypes = Object.freeze([registryProtocol.ociManifestMediaType, registryProtocol.dockerManifestMediaType]);

export function parseRpc1ServerProof(receiptBytes, manifestBytes, expectedProducer, expectedReference) {
  try {
    return parseProof(receiptBytes, manifestBytes, expectedProducer, expectedReference);
  } catch {
    throw new Error(invalidProof);
  }
}

export async function verifyRpc1ServerProof(receiptPath, expectedProducer, expectedReference) {
  try {
    const directory = path.dirname(path.resolve(receiptPath));
    if (path.basename(receiptPath) !== receiptName || !path.isAbsolute(receiptPath)) throw new Error(invalidProof);
    await verifyPrivateDirectory(directory);
    const receiptBytes = await readPrivateFile(directory, receiptName, maximumReceiptBytes);
    const receipt = parseUniqueJson(receiptBytes, maximumReceiptBytes);
    const manifestBytes = await readPrivateFile(directory, manifestName, maximumManifestBytes);
    const proof = parseRpc1ServerProof(receiptBytes, manifestBytes, expectedProducer, expectedReference);
    const inventoryBytes = await readPrivateFile(directory, inventoryName, maximumInventoryBytes);
    if (sha256(inventoryBytes) !== receipt.imageSource.inventoryFileSha256) throw new Error(invalidProof);
    const rows = verifyInventory(inventoryBytes, receipt.imageSource);
    await verifyRetainedArchive(directory, archiveName, rows, receipt.imageSource.archiveBytes,
      receipt.imageSource.archiveSha256, rpc1ServerSource.revision);
    return proof;
  } catch {
    throw new Error(invalidProof);
  }
}

function parseProof(receiptBytes, manifestBytes, expectedProducer, expectedReference) {
  if (!Buffer.isBuffer(receiptBytes) || receiptBytes.length === 0 || receiptBytes.length > maximumReceiptBytes
    || !Buffer.isBuffer(manifestBytes) || manifestBytes.length === 0 || manifestBytes.length > maximumManifestBytes) throw new Error(invalidProof);
  const receipt = parseUniqueJson(receiptBytes, maximumReceiptBytes);
  exactKeys(receipt, receiptKeys);
  if (receipt.schemaVersion !== 1 || receipt.kind !== 'keyload.rpc1-epoch6-server-image-proof.v1') throw new Error(invalidProof);
  verifyProducer(receipt.producer, expectedProducer);
  verifySourceMetadata(receipt.imageSource);
  verifyProtocolProfile(receipt.protocolProfile);
  verifyBases(receipt.bases);
  const manifest = verifyImage(receipt.image, manifestBytes, expectedReference, receipt.producer);
  return Object.freeze({ reference: manifest.finalReference, receipt });
}

export function createRpc1ExpectedProducer(environment, context) {
  const workflow = environment.GITHUB_WORKFLOW;
  if (typeof workflow !== 'string' || workflow.length === 0 || workflow.length > 256
    || workflow.trim() !== workflow || /[\u0000-\u001f\u007f]/.test(workflow) || environment.GITHUB_JOB !== 'docker-rf3'
    || context.sourceSha === rpc1ServerSource.revision || context.repository !== imageBuild.repository) throw new Error(invalidProof);
  return Object.freeze({ sourceSha: context.sourceSha, runId: context.runId, runAttempt: context.runAttempt,
    repository: context.repository, ref: context.ref, workflow, job: environment.GITHUB_JOB });
}

function verifyProtocolProfile(profile) {
  exactKeys(profile, profileKeys);
  if (profile.dataEpoch !== 6 || profile.requestInterfaceAlias !== 'keyload.request.v1'
    || profile.requestInterfaceVersion !== 1 || profile.peerEnvelopeVersion !== 2) throw new Error(invalidProof);
}

function verifyProducer(actual, expected) {
  exactKeys(actual, producerKeys);
  exactKeys(expected, producerKeys);
  for (const key of producerKeys) {
    if (typeof expected[key] !== 'string' || expected[key].length === 0 || actual[key] !== expected[key]) throw new Error(invalidProof);
  }
  if (!validation.shaPattern.test(actual.sourceSha) || actual.sourceSha !== actual.sourceSha.toLowerCase()
    || actual.sourceSha === rpc1ServerSource.revision || !validation.numericIdPattern.test(actual.runId)
    || !validation.numericIdPattern.test(actual.runAttempt) || !validation.repositoryPattern.test(actual.repository)
    || actual.repository !== imageBuild.repository || actual.repository.length > validation.maxRepositoryLength || !validation.refPattern.test(actual.ref)
    || actual.ref.length > validation.maxRefLength || !safeString(actual.workflow, 256) || actual.job !== 'docker-rf3') {
    throw new Error(invalidProof);
  }
}

function safeString(value, maximumLength) {
  return typeof value === 'string' && value.length > 0 && value.length <= maximumLength
    && value.trim() === value && !validation.controlCharacterPattern.test(value);
}

function verifySourceMetadata(source) {
  exactKeys(source, sourceKeys);
  if (source.revision !== rpc1ServerSource.revision || source.treeSha !== rpc1ServerSource.treeSha
    || source.archiveFile !== archiveName || source.inventoryFile !== inventoryName
    || source.sourceInventorySha256 !== rpc1ServerSource.sourceInventorySha256
    || source.fileCount !== rpc1ServerSource.fileCount || source.expandedBytes !== rpc1ServerSource.expandedBytes
    || source.archiveBytes !== rpc1ServerSource.archiveBytes || source.archiveSha256 !== rpc1ServerSource.archiveSha256
    || source.overlayCount !== 0 || !sha256Pattern.test(source.archiveSha256) || !sha256Pattern.test(source.inventoryFileSha256)
    || !Number.isSafeInteger(source.archiveBytes) || source.archiveBytes <= 0 || source.archiveBytes > maximumArchiveBytes) {
    throw new Error(invalidProof);
  }
}

function verifyBases(bases) {
  exactKeys(bases, baseKeys);
  if (bases.sdk !== baseImage.sdk || bases.runtime !== baseImage.aspnet || bases.registry !== registry.image) throw new Error(invalidProof);
}

function verifyImage(image, manifestBytes, expectedReference, producer) {
  exactKeys(image, imageKeys);
  if (image.name !== 'server' || image.manifestFile !== manifestName || image.revisionLabel !== rpc1ServerSource.revision
    || typeof image.contentType !== 'string' || !manifestTypes.includes(image.contentType)
    || !digestPattern.test(image.manifestSha256) || image.registryDigest !== image.manifestSha256
    || !digestPattern.test(image.configId) || image.configId === image.manifestSha256) throw new Error(invalidProof);
  const tag = `${rpc1ServerSource.revision}-${producer.runId}-${producer.runAttempt}`;
  const expectedTagged = `${imageReference.repositoryPrefix}server:${tag}`;
  if (image.taggedReference !== expectedTagged || !imageTagPattern.test(image.taggedReference)
    || typeof expectedReference !== 'string' || image.reference !== expectedReference
    || !validation.imageReferencePattern.test(image.reference)
    || image.reference !== `${image.taggedReference}@${image.registryDigest}`) throw new Error(invalidProof);
  const manifestDocument = parseUniqueJson(manifestBytes, maximumManifestBytes);
  verifyNativeManifest(manifestDocument, image.contentType);
  const evidence = parseManifestEvidence(manifestBytes, image.registryDigest, image.contentType,
    rpc1ServerSource.revision, image.configId, imageReference.serverName, image.taggedReference);
  if (evidence.finalReference !== image.reference || evidence.manifest.manifestSha256 !== image.manifestSha256) throw new Error(invalidProof);
  return evidence;
}

function verifyInventory(bytes, source) {
  const inventory = parseUniqueJson(bytes, maximumInventoryBytes);
  exactKeys(inventory, inventoryKeys);
  if (inventory.schemaVersion !== 1 || !Array.isArray(inventory.files) || inventory.files.length !== rpc1ServerSource.fileCount) {
    throw new Error(invalidProof);
  }
  let expandedBytes = 0;
  let pathTotalBytes = 0;
  let previousPath;
  for (const row of inventory.files) {
    exactKeys(row, inventoryRowKeys);
    if (typeof row.path !== 'string' || row.path.length === 0 || Buffer.from(row.path, 'utf8').toString('utf8') !== row.path
      || row.path.includes('\\') || row.path.startsWith('/')
      || row.path.split('/').some(part => part === '' || part === '.' || part === '..')
      || Buffer.byteLength(row.path, 'utf8') > 4096 || row.path.split('/').length > 64
      || ![0o644, 0o755].includes(row.mode) || !Number.isSafeInteger(row.bytes) || row.bytes < 0
      || !sha256Pattern.test(row.sha256)) throw new Error(invalidProof);
    if (previousPath && Buffer.compare(Buffer.from(previousPath, 'utf8'), Buffer.from(row.path, 'utf8')) >= 0) throw new Error(invalidProof);
    previousPath = row.path;
    expandedBytes += row.bytes;
    pathTotalBytes += Buffer.byteLength(row.path, 'utf8');
    if (!Number.isSafeInteger(expandedBytes) || expandedBytes > maximumArchiveBytes || pathTotalBytes > 16 * 1024 * 1024) {
      throw new Error(invalidProof);
    }
  }
  if (expandedBytes !== source.expandedBytes || hashRpc1Inventory(inventory.files) !== source.sourceInventorySha256) throw new Error(invalidProof);
  return inventory.files;
}

function verifyNativeManifest(manifest, responseType) {
  if (manifest === null || typeof manifest !== 'object' || Array.isArray(manifest)) throw new Error(invalidProof);
  const allowedRoot = ['schemaVersion', 'config', 'layers', 'mediaType', 'annotations'];
  if (Object.keys(manifest).some(key => !allowedRoot.includes(key)) || manifest.schemaVersion !== 2
    || !Array.isArray(manifest.layers) || manifest.layers.length < 1 || manifest.layers.length > maximumLayers
    || (manifest.mediaType !== undefined && manifest.mediaType !== responseType)) throw new Error(invalidProof);
  verifyAnnotations(manifest.annotations);
  const oci = responseType === registryProtocol.ociManifestMediaType;
  const configType = oci ? 'application/vnd.oci.image.config.v1+json' : 'application/vnd.docker.container.image.v1+json';
  verifyDescriptor(manifest.config, new Set([configType]));
  const layerTypes = oci
    ? new Set(['application/vnd.oci.image.layer.v1.tar', 'application/vnd.oci.image.layer.v1.tar+gzip', 'application/vnd.oci.image.layer.v1.tar+zstd'])
    : new Set(['application/vnd.docker.image.rootfs.diff.tar.gzip']);
  for (const layer of manifest.layers) verifyDescriptor(layer, layerTypes);
}

function verifyDescriptor(descriptor, mediaTypes) {
  if (descriptor === null || typeof descriptor !== 'object' || Array.isArray(descriptor)) throw new Error(invalidProof);
  const allowed = ['mediaType', 'size', 'digest', 'annotations'];
  if (Object.keys(descriptor).some(key => !allowed.includes(key)) || typeof descriptor.mediaType !== 'string'
    || !mediaTypes.has(descriptor.mediaType) || !Number.isSafeInteger(descriptor.size) || descriptor.size < 0
    || !digestPattern.test(descriptor.digest)) throw new Error(invalidProof);
  verifyAnnotations(descriptor.annotations);
}

function verifyAnnotations(annotations) {
  if (annotations === undefined) return;
  if (annotations === null || typeof annotations !== 'object' || Array.isArray(annotations)
    || Object.keys(annotations).length > maximumAnnotationEntries) throw new Error(invalidProof);
  for (const [key, value] of Object.entries(annotations)) {
    if (key.length === 0 || Buffer.from(key, 'utf8').toString('utf8') !== key
      || Buffer.byteLength(key, 'utf8') > maximumAnnotationKeyBytes || typeof value !== 'string'
      || Buffer.from(value, 'utf8').toString('utf8') !== value
      || Buffer.byteLength(value, 'utf8') > maximumAnnotationValueBytes) throw new Error(invalidProof);
  }
}

function parseUniqueJson(bytes, maximumBytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > maximumBytes) throw new Error(invalidProof);
  return parseBytes(bytes);
}

function exactKeys(value, keys) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error(invalidProof);
  const actual = Object.keys(value);
  if (actual.length !== keys.length || actual.some(key => !keys.includes(key))) throw new Error(invalidProof);
}

async function verifyPrivateDirectory(directory) {
  const info = await lstat(directory);
  if (!info.isDirectory() || info.isSymbolicLink() || (info.mode & 0o077) !== 0 || info.uid !== process.getuid()) throw new Error(invalidProof);
}

async function readPrivateFile(directory, name, maximumBytes) {
  const target = path.join(directory, name);
  const before = await lstat(target);
  if (!before.isFile() || before.isSymbolicLink() || before.size === 0 || before.size > maximumBytes
    || (before.mode & 0o077) !== 0 || before.uid !== process.getuid()) throw new Error(invalidProof);
  const handle = await open(target, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  try {
    const after = await handle.stat();
    if (!after.isFile() || after.dev !== before.dev || after.ino !== before.ino || after.size !== before.size) throw new Error(invalidProof);
    return await readBoundedHandle(handle, before.size, maximumBytes);
  } finally { await handle.close(); }
}

async function readBoundedHandle(handle, length, maximumBytes) {
  if (!Number.isSafeInteger(length) || length <= 0 || length > maximumBytes) throw new Error(invalidProof);
  const bytes = Buffer.allocUnsafe(length);
  let offset = 0;
  while (offset < length) {
    const { bytesRead } = await handle.read(bytes, offset, length - offset, offset);
    if (bytesRead === 0) throw new Error(invalidProof);
    offset += bytesRead;
  }
  const extra = Buffer.allocUnsafe(1);
  if ((await handle.read(extra, 0, 1, offset)).bytesRead !== 0 || (await handle.stat()).size !== length) throw new Error(invalidProof);
  return bytes;
}

function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
