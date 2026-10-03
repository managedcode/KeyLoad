import path from 'node:path';
import { runBounded } from './image-process.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { writeCapture } from './isolated-github-files.mjs';
import { streamToFile } from './isolated-github-stream.mjs';

export function validateZipInventory(output, requiredEntries) {
  requireGitHub(typeof output === 'string' && Buffer.byteLength(output) <= GH.inventoryBytes);
  const entries = output.split(/\r?\n/).filter(Boolean);
  requireGitHub(entries.length > 0 && entries.length <= GH.inventoryEntries && new Set(entries).size === entries.length);
  requireGitHub(entries.every(entry => /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*\/?$/.test(entry)
    && !entry.split('/').some(part => part === '.' || part === '..')));
  requireGitHub(requiredEntries.every(entry => entries.filter(value => value === entry).length === 1));
  return entries;
}

export async function inspectNativeZip(archive, requiredEntries, retainedInventory, context) {
  const result = await runBounded('unzip', ['-Z1', archive], { cwd: context.native.workspace,
    timeoutMs: GH.unzipTimeoutMs, maximumOutputBytes: GH.inventoryBytes });
  requireGitHub(result.success);
  const entries = validateZipInventory(result.stdout, requiredEntries);
  await writeCapture(retainedInventory, Buffer.from(result.stdout));
  return entries;
}

export const extractNativeEntry = (archive, entry, output, maximumBytes, context) => streamToFile('unzip',
  ['-p', archive, entry], path.resolve(output), maximumBytes, GH.unzipTimeoutMs, context.native.workspace);
