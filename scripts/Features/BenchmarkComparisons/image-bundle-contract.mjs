export const bundleFile = Object.freeze({
  descriptor: 'image-bundle.json', buildReceipt: 'build-image-receipt.json',
  server: 'server.tar', comparisons: 'comparisons.tar',
});
export const bundleDirectory = 'keyload-image-bundle';
export const retainedDirectory = 'keyload-images-build';
export const bundleLimit = Object.freeze({ archiveBytes: 4294967296, jsonBytes: 4194304, chunkBytes: 1048576 });
export const bundleError = Object.freeze({
  input: 'The image bundle input is invalid.',
  path: 'The image bundle path is unsafe.',
  exists: 'Image bundle output already exists.',
  native: 'The native image bundle operation failed.',
  identity: 'Native imported image identity differs from the build.',
});
export const imageNames = Object.freeze(['server', 'comparisons']);

export function requireBundle(condition, failure = bundleError.input) {
  if (!condition) throw new Error(failure);
}

export function requireKeys(value, keys) {
  requireBundle(value !== null && typeof value === 'object' && !Array.isArray(value));
  const actual = Object.keys(value);
  requireBundle(actual.length === keys.length && keys.every(key => Object.hasOwn(value, key)));
}

export function bundleDiagnostic(error) {
  return Object.values(bundleError).includes(error?.message) ? error.message : bundleError.native;
}
