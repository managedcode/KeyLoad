import path from 'node:path';
import { bundleError, requireBundle } from './image-bundle-contract.mjs';

export function parseImportArguments(argv) {
  requireBundle(Array.isArray(argv) && argv.length === 1 && typeof argv[0] === 'string'
    && argv[0].startsWith('--bundle='), bundleError.input);
  const directory = argv[0].slice('--bundle='.length);
  requireBundle(path.isAbsolute(directory) && directory.trim() === directory
    && !/[\x00-\x1f\x7f]/.test(directory), bundleError.path);
  return directory;
}
