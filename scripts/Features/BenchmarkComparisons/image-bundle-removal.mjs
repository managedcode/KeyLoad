import { buildArgument, processLimit } from './image-contracts.mjs';
import { parseBytes } from './aggregate-json.mjs';
import { parseImageMetadata } from './image-manifest.mjs';
import { bundleError, imageNames, requireBundle } from './image-bundle-contract.mjs';
import { requireNative } from './image-bundle-native.mjs';

const ownershipTemplate = `${buildArgument.configLabelTemplate}|{{json .RepoTags}}`;

async function requireOnlyOwnedTag(context, image) {
  const output = await requireNative(context, `bundle-own-tag-${image.name}`, [
    'image', 'inspect', '--format', ownershipTemplate, image.taggedReference,
  ], processLimit.inspectTimeoutMs);
  const parts = output.trim().split('|');
  requireBundle(parts.length === 3, bundleError.identity);
  const metadata = parseImageMetadata(parts.slice(0, 2).join('|'), context.sourceSha);
  requireBundle(metadata.configImageId === image.record.configId, bundleError.identity);
  const tags = parseBytes(Buffer.from(parts[2]));
  requireBundle(Array.isArray(tags) && tags.length === 1 && tags[0] === image.taggedReference, bundleError.identity);
}

export async function removeRoundTripOwnedImages(context, prepared) {
  // Validate both before deleting either; never force-delete a config with a foreign tag.
  for (const name of imageNames) await requireOnlyOwnedTag(context, prepared.images[name]);
  for (const name of imageNames) {
    await requireNative(context, `bundle-remove-tag-${name}`, [
      'image', 'rm', prepared.images[name].taggedReference,
    ], processLimit.cleanupTimeoutMs);
  }
  const output = await requireNative(context, 'bundle-prove-native-absence', [
    'image', 'ls', '--all', '--no-trunc', '--format', '{{.ID}}',
  ], processLimit.inspectTimeoutMs);
  const ids = output.trim().split(/\r?\n/).filter(Boolean);
  requireBundle(ids.every(id => /^sha256:[a-f0-9]{64}$/.test(id)), bundleError.identity);
  requireBundle(imageNames.every(name => !ids.includes(prepared.images[name].record.configId)), bundleError.identity);
}
