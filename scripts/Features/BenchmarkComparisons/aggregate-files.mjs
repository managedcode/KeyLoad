import { constants, createReadStream } from 'node:fs';
import { lstat, realpath } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { isAbsolute, resolve } from 'node:path';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';
export { parseBytes } from './aggregate-json.mjs';

const INPUT = AGGREGATE.errors.input;
export function absolutePath(value, error = INPUT) {
  requireValue(typeof value === 'string' && isAbsolute(value) && !value.includes('\0')
    && !value.split(/[\\/]/).some(part => part === '.' || part === '..') && resolve(value) === value, error);
  return value;
}
export async function existingPath(path, directory, error = INPUT) {
  absolutePath(path, error);
  const info = await lstat(path);
  requireValue(!info.isSymbolicLink() && (directory ? info.isDirectory() : info.isFile())
    && await realpath(path) === path, error);
  return info;
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
  const after = await lstat(path);
  requireValue(after.isFile() && !after.isSymbolicLink() && after.ino === info.ino && after.dev === info.dev
    && after.size === info.size && after.mtimeMs === info.mtimeMs, INPUT);
  return Buffer.concat(chunks, size);
}
export const hashBytes = bytes => createHash(AGGREGATE.hash).update(bytes).digest('hex');
