import { pathToFileURL } from 'node:url';
import { constants } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import path from 'node:path';
import { baseImage, imageKind, imageReference, outputFormat, registry, safeErrorMessage } from '../BenchmarkComparisons/image-contracts.mjs';
import { createRunContext, validateEntryArguments } from '../BenchmarkComparisons/image-inputs.mjs';
import { createImmutableManifest, ensureEvidenceDirectory } from '../BenchmarkComparisons/image-evidence.mjs';
import { buildProductImage, makeTaggedReference, pushProductImage, verifyBuildx, verifyDockerEngine, verifyDockerfilePins } from '../BenchmarkComparisons/image-engine.mjs';
import { requireRunningOwnedRegistry } from '../BenchmarkComparisons/cleanup-images.mjs';
import { fetchManifest } from '../BenchmarkComparisons/image-manifest.mjs';
import { verifySourceCheckout } from '../BenchmarkComparisons/prepare-images.mjs';
import { createInterface3ExpectedProducer, parseInterface3ServerProof } from './interface3-server-proof.mjs';
import { cleanupInterface3ServerSource, createInterface3ServerSource, interface3ServerSource,
  verifyInterface3ServerExport, verifyRetainedInterface3Archive } from './interface3-server-source.mjs';

const receiptName = 'interface3-epoch7-server-image-receipt.json';
const manifestName = 'interface3-epoch7-server-manifest.json';
const invalidImage = 'The immutable interface3 server image could not be prepared.';
const serverOutputName = 'KEYLOAD_INTERFACE3_SERVER_IMAGE';
const receiptOutputName = 'KEYLOAD_INTERFACE3_IMAGE_RECEIPT';
const manifestOutputName = 'KEYLOAD_INTERFACE3_SERVER_MANIFEST';

export async function prepareInterface3ServerImage(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  const producer = createInterface3ExpectedProducer(environment, context);
  await ensureEvidenceDirectory(context);
  await verifySourceCheckout(context);
  await verifyDockerfilePins(context.workspace);
  await verifyDockerEngine(context);
  await verifyBuildx(context);
  await requireRunningOwnedRegistry(context);

  let source;
  let primary;
  try {
    source = await createInterface3ServerSource(context);
    await verifyDockerfilePins(source.workspace);
    await verifyDockerfilePins(context.workspace);
    await verifyInterface3ServerExport(source);
    const tag = `${interface3ServerSource.revision}-${context.runId}-${context.runAttempt}`;
    const taggedReference = makeTaggedReference(imageKind.server, tag);
    const built = await buildProductImage(context, imageKind.server, taggedReference,
      { workspace: source.workspace, sourceSha: interface3ServerSource.revision });
    await verifyInterface3ServerExport(source);
    await verifyRetainedInterface3Archive(context, {
      treeSha: source.treeSha,
      archiveBytes: source.archiveBytes,
      archiveSha256: source.archiveSha256,
      fileCount: source.inventory.fileCount,
      expandedBytes: source.inventory.expandedBytes,
      sourceInventorySha256: source.inventory.sourceInventorySha256,
    }, source.inventory.files);
    await pushProductImage(context, taggedReference);
    const manifest = await fetchManifest(context, imageReference.serverName, tag,
      { ...built, sourceRevision: interface3ServerSource.revision, taggedReference });
    const contentType = manifest.manifest.contentTypeHeader.split(';')[0].trim().toLowerCase();
    if (manifest.manifestBytes.length === 0 || manifest.manifestBytes.length > 1024 * 1024) throw new Error(invalidImage);
    const receipt = Object.freeze({
      schemaVersion: 1,
      kind: 'keyload.interface3-epoch7-server-image-proof.v1',
      producer,
      imageSource: Object.freeze({ revision: interface3ServerSource.revision, treeSha: source.treeSha,
        archiveFile: interface3ServerSource.archiveName, archiveSha256: source.archiveSha256,
        archiveBytes: source.archiveBytes, inventoryFile: interface3ServerSource.inventoryName,
        inventoryFileSha256: source.inventoryFileSha256, sourceInventorySha256: source.inventory.sourceInventorySha256,
        fileCount: source.inventory.fileCount, expandedBytes: source.inventory.expandedBytes, overlayCount: 0 }),
      protocolProfile: Object.freeze({ dataEpoch: interface3ServerSource.dataEpoch,
        requestInterfaceAlias: interface3ServerSource.requestInterfaceAlias, requestInterfaceVersion: interface3ServerSource.requestInterfaceVersion,
        peerEnvelopeVersion: interface3ServerSource.peerEnvelopeVersion }),
      bases: Object.freeze({ sdk: baseImage.sdk, runtime: baseImage.aspnet, registry: registry.image }),
      image: Object.freeze({ name: 'server', taggedReference, reference: manifest.finalReference,
        manifestFile: manifestName, manifestSha256: manifest.manifest.manifestSha256,
        registryDigest: manifest.manifest.digestHeader, contentType, configId: built.configImageId,
        revisionLabel: interface3ServerSource.revision }),
    });
    const receiptBytes = Buffer.from(`${JSON.stringify(receipt, null, outputFormat.jsonIndent)}\n`, 'utf8');
    if (receiptBytes.length === 0 || receiptBytes.length > 64 * 1024) throw new Error(invalidImage);
    parseInterface3ServerProof(receiptBytes, manifest.manifestBytes, producer, manifest.finalReference);
    await createImmutableManifest(context, manifestName, manifest.manifestBytes);
    await writeExclusive(context.evidenceDirectory, receiptName, receiptBytes);
    await appendOutputs(context.githubOutput, Object.freeze({
      [serverOutputName]: manifest.finalReference,
      [receiptOutputName]: path.join(context.evidenceDirectory, receiptName),
      [manifestOutputName]: path.join(context.evidenceDirectory, manifestName),
      KEYLOAD_INTERFACE3_SERVER_INVENTORY: path.join(context.evidenceDirectory, interface3ServerSource.inventoryName),
      KEYLOAD_INTERFACE3_SERVER_ARCHIVE: path.join(context.evidenceDirectory, interface3ServerSource.archiveName),
    }));
    return Object.freeze({ reference: manifest.finalReference, receipt, inventorySha256: source.inventory.sourceInventorySha256 });
  } catch (error) {
    primary = error;
    throw error;
  } finally {
    if (source && primary?.preserveOwnedPaths !== true) await cleanupWithPrimary(source, primary);
  }
}

async function appendOutputs(file, values) {
  const before = await lstat(file);
  if (!before.isFile() || before.isSymbolicLink() || before.size > 256 * 1024) throw new Error(invalidImage);
  const lines = Object.entries(values).map(([key, value]) => `${key}=${value}\n`).join('');
  if (Object.entries(values).some(([key, value]) => !/^[A-Z][A-Z0-9_]{0,63}$/.test(key)
    || typeof value !== 'string' || /[\u0000-\u001f\u007f]/.test(value))
    || before.size + Buffer.byteLength(lines, 'utf8') > 256 * 1024) throw new Error(invalidImage);
  const handle = await open(file, constants.O_WRONLY | constants.O_APPEND | constants.O_NOFOLLOW);
  try {
    const current = await handle.stat();
    if (!current.isFile() || current.dev !== before.dev || current.ino !== before.ino || current.size !== before.size) throw new Error(invalidImage);
    await handle.writeFile(lines, 'utf8');
    await handle.sync();
  } finally { await handle.close(); }
}

async function writeExclusive(directory, name, bytes) {
  const target = path.resolve(directory, name);
  if (path.dirname(target) !== directory || !Buffer.isBuffer(bytes)) throw new Error(invalidImage);
  const handle = await open(target, 'wx', 0o600);
  try { await handle.writeFile(bytes); await handle.sync(); }
  finally { await handle.close(); }
}

async function cleanupWithPrimary(source, primary) {
  try { await cleanupInterface3ServerSource(source); }
  catch (cleanup) {
    if (primary) throw new AggregateError([primary, cleanup], 'Interface3 image preparation and source cleanup failed.');
    throw cleanup;
  }
}


if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await prepareInterface3ServerImage(); }
  catch (error) {
    process.stderr.write(`${safeErrorMessage(error, invalidImage)}\n`);
    process.exitCode = 1;
  }
}
