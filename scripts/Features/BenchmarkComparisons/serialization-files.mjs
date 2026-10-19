import { constants } from 'node:fs';
import { copyFile, lstat, mkdir, open, readdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, join, relative, resolve } from 'node:path';
import { EXECUTION, runtimeRequire } from './serialization-contract.mjs';

export const hashBytes = bytes => createHash('sha256').update(bytes).digest('hex');
export const hashObject = value => hashBytes(Buffer.from(JSON.stringify(value)));

export async function readBounded(path, limit = EXECUTION.reportBytes, allowEmpty = false) {
  const handle = await open(path, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0));
  try {
    const info = await handle.stat();
    runtimeRequire(info.isFile() && info.size <= limit && (allowEmpty || info.size > 0), 'file.bounds');
    const bytes = await handle.readFile();
    runtimeRequire(bytes.length === info.size, 'file.changed');
    return bytes;
  } finally { await handle.close(); }
}

export async function fileFact(path, name = path, allowEmpty = false) {
  const bytes = await readBounded(path, EXECUTION.fileBytes, allowEmpty);
  return { path: name, bytes: bytes.length, sha256: hashBytes(bytes) };
}

export async function writeJson(path, value) {
  await writeFile(path, `${JSON.stringify(value, null, 2)}\n`, { flag: 'wx' });
}

export async function requireDirectory(path) {
  const info = await lstat(path);
  runtimeRequire(info.isDirectory() && !info.isSymbolicLink(), 'directory');
}

export async function walkFiles(root, current = root, files = []) {
  await requireDirectory(current);
  const entries = await readdir(current, { withFileTypes: true });
  for (const entry of entries) {
    runtimeRequire(!entry.isSymbolicLink(), 'file.symlink');
    const path = join(current, entry.name);
    if (entry.isDirectory()) await walkFiles(root, path, files);
    else {
      runtimeRequire(entry.isFile() && files.length < EXECUTION.maximumFiles, 'file.inventory');
      files.push(relative(root, path));
    }
  }
  return files.sort();
}

export async function collectFacts(root, paths, allowEmpty = []) {
  runtimeRequire(paths.length <= EXECUTION.maximumFiles && new Set(paths).size === paths.length, 'file.inventory');
  const facts = [];
  let total = 0;
  for (const path of [...paths].sort()) {
    runtimeRequire(!path.startsWith('/') && !path.split('/').includes('..'), 'file.path');
    const fact = await fileFact(join(root, path), path, allowEmpty.includes(path));
    total += fact.bytes;
    runtimeRequire(total <= EXECUTION.totalBytes, 'file.totalBytes');
    facts.push(fact);
  }
  return facts;
}

export async function copyOriginal(source, destination) {
  const before = await fileFact(source);
  await mkdir(dirname(destination), { recursive: true });
  await copyFile(source, destination, constants.COPYFILE_EXCL);
  const after = await fileFact(destination);
  runtimeRequire(before.sha256 === after.sha256 && before.bytes === after.bytes, 'file.copy');
}

export function ownedOutput(environment, value) {
  runtimeRequire(typeof environment.GITHUB_WORKSPACE === 'string' && resolve(environment.GITHUB_WORKSPACE) === process.cwd(), 'workspace');
  const directory = resolve(value);
  runtimeRequire(directory === resolve(EXECUTION.output)
    && environment.KEYLOAD__SERIALIZATION_CORPUS_DIRECTORY === join(directory, 'corpus'), 'output.path');
  return directory;
}
