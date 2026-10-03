import path from 'node:path';
import { buildArgument, processLimit } from './image-contracts.mjs';
import { runDocker } from './image-engine.mjs';
import { parseImageMetadata } from './image-manifest.mjs';
import { bundleError, bundleFile, requireBundle } from './image-bundle-contract.mjs';
import { hashArchive, reserveArchive } from './image-bundle-files.mjs';

export async function requireNative(context, operation, args, timeoutMs = processLimit.commandTimeoutMs) {
  const result = await runDocker(context, args, { operation, timeoutMs });
  requireBundle(result.success, bundleError.native);
  return result.stdout;
}

export async function verifyNativeImage(context, image) {
  const output = await requireNative(context, `bundle-inspect-${image.name}`, [
    'image', 'inspect', '--format', buildArgument.configLabelTemplate, image.taggedReference,
  ], processLimit.inspectTimeoutMs);
  const metadata = parseImageMetadata(output, context.sourceSha);
  requireBundle(metadata.configImageId === image.record.configId, bundleError.identity);
  return Object.freeze({ taggedReference: image.taggedReference, ...metadata });
}

export async function saveNativeImage(context, directory, image) {
  await verifyNativeImage(context, image);
  const target = path.join(directory, bundleFile[image.name]);
  await reserveArchive(target);
  await requireNative(context, `bundle-save-${image.name}`, [
    'image', 'save', '--output', target, image.taggedReference,
  ]);
  return Object.freeze({ archive: bundleFile[image.name], ...await hashArchive(target) });
}

export async function loadNativeImage(context, prepared, name) {
  await requireNative(context, `bundle-load-${name}`, [
    'image', 'load', '--input', path.join(prepared.directory, bundleFile[name]),
  ]);
  return verifyNativeImage(context, prepared.images[name]);
}
