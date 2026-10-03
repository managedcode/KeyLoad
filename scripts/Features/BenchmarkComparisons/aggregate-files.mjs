import { constants, createReadStream } from 'node:fs';
import { lstat, mkdir, readdir, realpath, rename, rm, writeFile } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';
export { parseBytes } from './aggregate-json.mjs';

const INPUT = AGGREGATE.errors.input;
const OUTPUT = AGGREGATE.errors.output;
const NAME = /^[a-zA-Z0-9][a-zA-Z0-9_.-]*$/;
const MAX_ENTRIES = 4096;
const MAX_DEPTH = 8;
const STAGING_PREFIX = '.isolated-aggregate-';

export function absolutePath(value, error = INPUT) {
  requireValue(typeof value === 'string' && isAbsolute(value) && !value.includes('\0') &&
    !value.split(/[\\/]/).some(part => part === '.' || part === '..') && resolve(value) === value, error);
  return value;
}

export async function existingPath(path, directory, error = INPUT) {
  absolutePath(path, error);
  const info = await lstat(path);
  requireValue(!info.isSymbolicLink() && (directory ? info.isDirectory() : info.isFile()) &&
    await realpath(path) === path, error);
  return info;
}

export async function assertAbsent(path) {
  try {
    await lstat(path);
  } catch (error) {
    if (error.code === 'ENOENT') return;
    throw error;
  }
  requireValue(false, OUTPUT);
}

export async function validatePaths(paths) {
  for (const path of Object.values(paths)) absolutePath(path);
  await existingPath(paths.input, true);
  await existingPath(paths.plan, false);
  await existingPath(paths.proof, false);
  await existingPath(dirname(paths.output), true, OUTPUT);
  const distance = relative(paths.input, paths.output);
  const reverse = relative(paths.output, paths.input);
  requireValue((distance === '..' || distance.startsWith('..' + sep)) &&
    (reverse === '..' || reverse.startsWith('..' + sep)), OUTPUT);
  await assertAbsent(paths.output);
}

export async function readBytes(path, limit) {
  const info = await existingPath(path, false);
  requireValue(info.size > 0 && info.size <= limit, INPUT);
  const stream = createReadStream(path, { flags: constants.O_RDONLY | constants.O_NOFOLLOW, highWaterMark: 65_536 });
  const chunks = [];
  let size = 0;
  for await (const chunk of stream) {
    size += chunk.length;
    requireValue(size <= limit, INPUT);
    chunks.push(chunk);
  }
  requireValue(size === info.size, INPUT);
  return Buffer.concat(chunks, size);
}

export const hashBytes = bytes => createHash(AGGREGATE.hash).update(bytes).digest('hex');
export const rawPath = id => [AGGREGATE.workers, id, AGGREGATE.raw].join('/');
export const rawFile = (root, id) => join(root, AGGREGATE.workers, id, AGGREGATE.raw);

async function validateTree(path, depth, budget) {
  requireValue(depth <= MAX_DEPTH, INPUT);
  for (const entry of await readdir(path, { withFileTypes: true })) {
    budget.count += 1;
    requireValue(budget.count <= MAX_ENTRIES && NAME.test(entry.name) && !entry.isSymbolicLink(), INPUT);
    const child = join(path, entry.name);
    if (entry.isDirectory()) await validateTree(child, depth + 1, budget);
    else await existingPath(child, false);
  }
}

export async function validateInventory(input, cells) {
  const roots = await readdir(input);
  requireValue(roots.length === 1 && roots[0] === AGGREGATE.workers, INPUT);
  const root = join(input, AGGREGATE.workers);
  await existingPath(root, true);
  const expected = new Set(cells.map(cell => cell.id));
  const entries = await readdir(root, { withFileTypes: true });
  requireValue(entries.length === expected.size && entries.every(entry =>
    expected.has(entry.name) && entry.isDirectory() && !entry.isSymbolicLink()), INPUT);
  for (const entry of entries) await validateTree(join(root, entry.name), 0, { count: 0 });
}

export async function createStage(output) {
  const path = join(dirname(output), STAGING_PREFIX + randomUUID());
  await mkdir(path, { recursive: false });
  return path;
}

export async function retainBytes(stage, id, bytes) {
  const file = rawFile(stage, id);
  await mkdir(dirname(file), { recursive: true });
  await writeFile(file, bytes, { flag: 'wx' });
}

export async function publishStage(stage, output, manifest) {
  await writeFile(join(stage, AGGREGATE.manifest), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
  await assertAbsent(output);
  await rename(stage, output);
}

export const removeStage = stage => rm(stage, { recursive: true, force: true });
