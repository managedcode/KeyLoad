import { lstat, mkdir } from 'node:fs/promises';
import path from 'node:path';

const unsafe = 'The native artifact path is unsafe.';
export async function requireDirectory(directory) {
  if (typeof directory !== 'string' || !path.isAbsolute(directory) || /[\x00-\x1f\x7f]/.test(directory)) throw new Error(unsafe);
  const resolved = path.resolve(directory);
  let current = path.parse(resolved).root;
  for (const component of resolved.slice(current.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, component);
    const info = await lstat(current).catch(() => null);
    if (!info?.isDirectory() || info.isSymbolicLink()) throw new Error(unsafe);
  }
  return resolved;
}
export async function createDirectory(directory) {
  await requireDirectory(path.dirname(directory));
  await mkdir(directory, { mode: 0o700 }).catch(error => {
    throw new Error(error?.code === 'EEXIST' ? 'Native artifact output already exists.' : unsafe);
  });
  return requireDirectory(directory);
}
export async function requireOutputFile(target) {
  await requireDirectory(path.dirname(target));
  const info = await lstat(target).catch(() => null);
  if (!info?.isFile() || info.isSymbolicLink()) throw new Error(unsafe);
}
