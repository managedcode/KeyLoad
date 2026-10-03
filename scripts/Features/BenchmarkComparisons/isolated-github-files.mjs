import { constants, createReadStream } from 'node:fs';
import { chmod, lstat, open } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { existingPath, readBytes } from './aggregate-files.mjs';
import { parseBytes } from './aggregate-json.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';

export async function openExclusive(target) {
  await existingPath(path.dirname(target), true);
  const handle = await open(target, constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL | (constants.O_NOFOLLOW ?? 0), 0o600);
  try { await handle.chmod(0o400); return handle; } catch (error) { await handle.close(); throw error; }
}

export async function writeCapture(target, bytes) {
  requireGitHub(Buffer.isBuffer(bytes) && bytes.length <= GH.metadataBytes);
  const handle = await openExclusive(target);
  try {
    await handle.writeFile(bytes);
    await handle.sync();
    await handle.chmod(0o400);
  } finally { await handle.close(); }
}

export const writeJson = (target, value) => writeCapture(target, Buffer.from(`${JSON.stringify(value, null, 2)}\n`));
export const readJson = async (target, maximumBytes = GH.metadataBytes) => parseBytes(await readBytes(target, maximumBytes));

export async function hashRegularFile(target, maximumBytes) {
  const before = await existingPath(target, false);
  requireGitHub(before.size > 0 && before.size <= maximumBytes);
  const hash = createHash('sha256');
  let bytes = 0;
  const stream = createReadStream(target, { flags: constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0), highWaterMark: 65536 });
  for await (const chunk of stream) {
    bytes += chunk.length;
    requireGitHub(bytes <= maximumBytes);
    hash.update(chunk);
  }
  const after = await lstat(target);
  requireGitHub(bytes === before.size && after.isFile() && !after.isSymbolicLink() && before.ino === after.ino
    && before.dev === after.dev && before.size === after.size && before.mtimeMs === after.mtimeMs);
  return { bytes, sha256: hash.digest('hex') };
}

export const retainReadOnly = target => chmod(target, 0o400);
