import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import path from 'node:path';
import {
  baseImage, imageKind, imageReference, outputFormat, processLimit, registry, registryProtocol, safeErrorMessage,
} from '../BenchmarkComparisons/image-contracts.mjs';
import { createRunContext, validateEntryArguments } from '../BenchmarkComparisons/image-inputs.mjs';
import {
  buildProductImage, verifyBuildx, verifyDockerEngine, verifyDockerfilePins, makeTaggedReference, pushProductImage,
} from '../BenchmarkComparisons/image-engine.mjs';
import { requireRunningOwnedRegistry } from '../BenchmarkComparisons/cleanup-images.mjs';
import { fetchManifest } from '../BenchmarkComparisons/image-manifest.mjs';
import { ensureEvidenceDirectory } from '../BenchmarkComparisons/image-evidence.mjs';
import { verifySourceCheckout } from '../BenchmarkComparisons/prepare-images.mjs';
import { parseNative5ServerProof } from './native5-server-proof.mjs';
import {
  cleanupNative5ServerSource, createNative5ServerSource, native5ServerSource, verifyNative5ServerExport,
} from './native5-server-source.mjs';

const receiptName = 'prior-server-image-receipt.json';
const manifestName = 'prior-server-manifest.json';
const invalidProducer = 'The current GitHub producer identity is invalid.';
const commandFailure = 'The immutable native5 server image could not be prepared.';

export async function prepareNative5ServerImage(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  const producer = createExpectedProducer(environment, context);
  await ensureEvidenceDirectory(context);
  await verifySourceCheckout(context);
  await verifyDockerfilePins(context.workspace);
  await verifyDockerEngine(context);
  await verifyBuildx(context);
  await requireRunningOwnedRegistry(context);

  let source;
  let primary;
  try {
    source = await createNative5ServerSource(context);
    await verifyDockerfilePins(source.workspace);
    await verifyDockerfilePins(context.workspace);
    const tag = `${native5ServerSource.revision}-${context.runId}-${context.runAttempt}`;
    const taggedReference = makeTaggedReference(imageKind.server, tag);
    await verifyNative5ServerExport(source);
    const built = await buildProductImage(context, imageKind.server, taggedReference, {
      workspace: source.workspace,
      sourceSha: native5ServerSource.revision,
    });
    await pushProductImage(context, taggedReference);
    const manifest = await fetchManifest(context, imageReference.serverName, tag, {
      ...built,
      sourceRevision: native5ServerSource.revision,
      taggedReference,
    });
    const contentType = manifest.manifest.contentTypeHeader.split(';')[0].trim().toLowerCase();
    if (manifest.manifestBytes.length > 1024 * 1024
      || ![registryProtocol.ociManifestMediaType, registryProtocol.dockerManifestMediaType].includes(contentType)) {
      throw new Error(commandFailure);
    }
    const inventoryBytes = Buffer.from(`${JSON.stringify({ schemaVersion: 1, files: source.inventory.files }, null, 2)}\n`, 'utf8');
    if (inventoryBytes.length > 1024 * 1024) throw new Error(commandFailure);
    const receipt = Object.freeze({
      schemaVersion: 1,
      kind: 'keyload.prior-server-image-proof.v1',
      producer,
      imageSource: Object.freeze({
        revision: native5ServerSource.revision,
        treeSha: native5ServerSource.treeSha,
        archiveFile: native5ServerSource.archiveName,
        archiveSha256: source.archiveSha256,
        archiveBytes: source.archiveBytes,
        inventoryFile: native5ServerSource.inventoryName,
        inventoryFileSha256: sha256(inventoryBytes),
        sourceInventorySha256: source.inventory.sourceInventorySha256,
        fileCount: source.inventory.fileCount,
        expandedBytes: source.inventory.expandedBytes,
        overlayCount: 0,
      }),
      bases: Object.freeze({ sdk: baseImage.sdk, runtime: baseImage.aspnet, registry: registry.image }),
      image: Object.freeze({
        name: 'server',
        taggedReference,
        reference: manifest.finalReference,
        manifestFile: manifestName,
        manifestSha256: manifest.manifest.manifestSha256,
        registryDigest: manifest.manifest.digestHeader,
        contentType,
        configId: built.configImageId,
        revisionLabel: native5ServerSource.revision,
      }),
    });
    const receiptBytes = Buffer.from(`${JSON.stringify(receipt, null, outputFormat.jsonIndent)}${outputFormat.newline}`, outputFormat.utf8);
    if (receiptBytes.length > 64 * 1024) throw new Error(commandFailure);
    parseNative5ServerProof(receiptBytes, manifest.manifestBytes, producer, manifest.finalReference);
    await writeExclusive(context.evidenceDirectory, manifestName, manifest.manifestBytes);
    await writeExclusive(context.evidenceDirectory, receiptName, receiptBytes);
    await appendOutputs(context.githubOutput, Object.freeze({
      'prior-server-image': manifest.finalReference,
      'prior-image-receipt': path.join(context.evidenceDirectory, receiptName),
      'prior-server-manifest': path.join(context.evidenceDirectory, manifestName),
    }));
    return Object.freeze({ reference: manifest.finalReference, receipt, sourceInventorySha256: source.inventory.sourceInventorySha256 });
  } catch (error) {
    primary = error;
    throw error;
  } finally {
    if (source) await cleanupWithPrimary(source, primary);
  }
}

export function createExpectedProducer(environment, context) {
  const workflow = environment.GITHUB_WORKFLOW;
  const job = environment.GITHUB_JOB;
  if (typeof workflow !== 'string' || workflow.length === 0 || workflow.length > 256
    || workflow.trim() !== workflow || /[\u0000-\u001f\u007f]/.test(workflow) || job !== 'docker-rf3'
    || context.sourceSha === native5ServerSource.revision) {
    throw new Error(invalidProducer);
  }
  return Object.freeze({
    sourceSha: context.sourceSha,
    runId: context.runId,
    runAttempt: context.runAttempt,
    repository: context.repository,
    ref: context.ref,
    workflow,
    job,
  });
}

async function appendOutputs(file, values) {
  const info = await lstat(file);
  if (!info.isFile() || info.isSymbolicLink() || info.size > processLimit.maxOutputBytes) {
    throw new Error(commandFailure);
  }
  const lines = Object.entries(values).map(([key, value]) => `${key}=${value}${outputFormat.newline}`).join('');
  const lineBytes = Buffer.byteLength(lines, outputFormat.utf8);
  if (info.size + lineBytes > processLimit.maxOutputBytes || Object.entries(values).some(([key, value]) =>
    !/^[a-z][a-z0-9-]{0,63}$/.test(key) || typeof value !== 'string' || /[\u0000-\u001f\u007f]/.test(value))) {
    throw new Error(commandFailure);
  }
  const handle = await open(file, constants.O_WRONLY | constants.O_APPEND | constants.O_NOFOLLOW);
  try {
    const current = await handle.stat();
    if (!current.isFile() || current.dev !== info.dev || current.ino !== info.ino || current.size !== info.size) throw new Error(commandFailure);
    await handle.writeFile(lines, outputFormat.utf8);
    await handle.sync();
  }
  finally { await handle.close(); }
}

async function writeExclusive(directory, name, bytes) {
  const target = path.resolve(directory, name);
  if (path.dirname(target) !== directory || !Buffer.isBuffer(bytes)) throw new Error(commandFailure);
  const handle = await open(target, 'wx', 0o600);
  try { await handle.writeFile(bytes); await handle.sync(); }
  finally { await handle.close(); }
}

async function cleanupWithPrimary(source, primary) {
  try { await cleanupNative5ServerSource(source); }
  catch (cleanup) {
    if (primary) throw new AggregateError([primary, cleanup], 'Prior image failure and export cleanup failure.');
    throw cleanup;
  }
}

function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await prepareNative5ServerImage(); }
  catch (error) {
    process.stderr.write(`${safeErrorMessage(error, commandFailure)}${outputFormat.newline}`);
    process.exitCode = 1;
  }
}
