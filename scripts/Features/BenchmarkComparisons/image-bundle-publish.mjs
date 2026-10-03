import path from 'node:path';
import { fileName } from './image-contracts.mjs';
import { appendImageOutputs, createImmutableManifest, createImmutableReceipt, ensureEvidenceDirectory } from './image-evidence.mjs';
import { makeImageTag, pushProductImage, startOwnedRegistry } from './image-engine.mjs';
import { fetchManifest, waitForRegistry } from './image-manifest.mjs';
import { makeImageRecord } from './prepare-images.mjs';
import { bundleError, bundleFile, imageNames, requireBundle } from './image-bundle-contract.mjs';
import { requireAbsent, requireOutputFile, writeExclusive } from './image-bundle-files.mjs';

export async function prepareImportOutputs(context, prepared) {
  await requireOutputFile(context.githubOutput);
  await ensureEvidenceDirectory(context);
  const names = [fileName.receipt, fileName.serverManifest, fileName.comparisonsManifest, bundleFile.buildReceipt];
  for (const name of names) await requireAbsent(path.join(context.evidenceDirectory, name));
  await writeExclusive(path.join(context.evidenceDirectory, bundleFile.buildReceipt), prepared.receiptBytes);
}

export async function publishImportedImages(context, prepared, loaded) {
  await startOwnedRegistry(context);
  await waitForRegistry(context);
  const images = {};
  const manifests = {};
  for (const name of imageNames) {
    await pushProductImage(context, loaded[name].taggedReference);
    const manifest = await fetchManifest(context, name, makeImageTag(context), loaded[name]);
    requireBundle(manifest.manifestBytes.equals(prepared.images[name].bytes)
      && manifest.finalReference === prepared.images[name].record.reference, bundleError.identity);
    manifests[name] = manifest;
    images[name] = makeImageRecord(manifest, prepared.images[name].record.manifestFile, loaded[name]);
  }
  for (const name of imageNames) {
    await createImmutableManifest(context, prepared.images[name].record.manifestFile, manifests[name].manifestBytes);
  }
  await createImmutableReceipt(context, { ...prepared.receipt, images });
  await appendImageOutputs(context, images.server.reference, images.comparisons.reference);
}
