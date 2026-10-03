import path from 'node:path';
import { baseImage, fileName, registry } from './image-contracts.mjs';
import { makeImageTag, makeTaggedReference } from './image-engine.mjs';
import { parseManifestEvidence } from './image-manifest.mjs';
import { parseBytes } from './aggregate-json.mjs';
import { bundleFile, bundleLimit, imageNames, requireBundle, requireKeys } from './image-bundle-contract.mjs';
import { hashArchive, readRegular, requireDirectory } from './image-bundle-files.mjs';

function parseStrict(bytes) {
  try { return parseBytes(bytes); } catch { requireBundle(false); }
}

function validateCohort(value, context) {
  requireBundle(value.sourceRevision === context.sourceSha && value.runId === context.runId
    && value.attempt === context.runAttempt && value.repository === context.repository && value.ref === context.ref);
}

function validateReceipt(receipt, context) {
  requireKeys(receipt, ['schemaVersion', 'sourceRevision', 'github', 'bases', 'images']);
  requireKeys(receipt.github, ['runId', 'attempt', 'repository', 'ref']);
  requireKeys(receipt.bases, ['sdk', 'runtime', 'registry']);
  requireKeys(receipt.images, imageNames);
  requireBundle(receipt.schemaVersion === 1);
  validateCohort({ sourceRevision: receipt.sourceRevision, ...receipt.github }, context);
  requireBundle(receipt.bases.sdk === baseImage.sdk && receipt.bases.runtime === baseImage.aspnet
    && receipt.bases.registry === registry.image);
}

async function readImage(directory, name, context, record) {
  requireKeys(record, ['reference', 'manifestDigest', 'registryDigest', 'manifestFile', 'revisionLabel', 'configId']);
  const manifestFile = name === 'server' ? fileName.serverManifest : fileName.comparisonsManifest;
  requireBundle(record.manifestFile === manifestFile && record.revisionLabel === context.sourceSha
    && /^sha256:[a-f0-9]{64}$/.test(record.configId ?? '')
    && /^sha256:[a-f0-9]{64}$/.test(record.manifestDigest ?? '')
    && record.manifestDigest === record.registryDigest);
  const bytes = await readRegular(path.join(directory, manifestFile));
  const document = parseStrict(bytes);
  const taggedReference = makeTaggedReference(name, makeImageTag(context));
  const manifest = parseManifestEvidence(bytes, record.registryDigest, document.mediaType,
    context.sourceSha, record.configId, name, taggedReference);
  requireBundle(manifest.finalReference === record.reference);
  return Object.freeze({ name, record, bytes, manifest, taggedReference });
}

export async function readPreparedImages(directory, context) {
  const resolved = await requireDirectory(directory);
  const receiptBytes = await readRegular(path.join(resolved, fileName.receipt));
  const receipt = parseStrict(receiptBytes);
  validateReceipt(receipt, context);
  const images = {};
  for (const name of imageNames) images[name] = await readImage(resolved, name, context, receipt.images[name]);
  return Object.freeze({ directory: resolved, receipt, receiptBytes, images: Object.freeze(images) });
}

function validateBundle(bundle, context) {
  requireKeys(bundle, ['schemaVersion', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'images']);
  requireKeys(bundle.images, imageNames);
  requireBundle(bundle.schemaVersion === 1);
  validateCohort(bundle, context);
}

export async function readImageBundle(directory, context) {
  const prepared = await readPreparedImages(directory, context);
  const bundle = parseStrict(await readRegular(path.join(prepared.directory, bundleFile.descriptor)));
  validateBundle(bundle, context);
  for (const name of imageNames) {
    const record = bundle.images[name];
    requireKeys(record, ['archive', 'bytes', 'sha256']);
    requireBundle(record.archive === bundleFile[name] && Number.isSafeInteger(record.bytes)
      && record.bytes > 0 && record.bytes <= bundleLimit.archiveBytes && /^[a-f0-9]{64}$/.test(record.sha256 ?? ''));
    const actual = await hashArchive(path.join(prepared.directory, record.archive));
    requireBundle(actual.bytes === record.bytes && actual.sha256 === record.sha256);
  }
  return Object.freeze({ ...prepared, bundle });
}
