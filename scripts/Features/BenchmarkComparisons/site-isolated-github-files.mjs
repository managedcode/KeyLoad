import { readdir } from 'node:fs/promises';
import path from 'node:path';
import { existingPath } from './aggregate-files.mjs';
import { hashRegularFile } from './isolated-github-files.mjs';
import { SITE_GH, requireSite, safeRelative } from './site-isolated-github-contract.mjs';

export async function siteMetadataFiles(input) {
  const files = [];
  async function visit(relative, depth) {
    requireSite(depth <= 8);
    const root = path.join(input, relative);
    await existingPath(root, true);
    for (const entry of await readdir(root, { withFileTypes: true })) {
      const name = relative + '/' + entry.name;
      requireSite(safeRelative(name) && !entry.isSymbolicLink() && files.length < 100000);
      if (entry.isDirectory()) await visit(name, depth + 1);
      else files.push({ path: name, ...await hashRegularFile(path.join(input, name), SITE_GH.metadataBytes) });
    }
  }
  await visit(SITE_GH.metadata, 0);
  return files.sort((left, right) => left.path.localeCompare(right.path));
}

export async function verifySiteFiles(input, files) {
  for (const file of files) {
    requireSite(safeRelative(file.path));
    const actual = await hashRegularFile(path.join(input, file.path), file.bytes);
    requireSite(actual.bytes === file.bytes && actual.sha256 === file.sha256);
  }
}
