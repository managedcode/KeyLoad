import { lstat, mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { RESULT_NAME, compareResults, resultFileName, resultKey, validateResult } from './results-contract.mjs';

// Every file named like a result joins the merge, at any depth; job test logs beside it are not results.
async function findResultFiles(root, directory, found) {
  const entries = await readdir(directory, { withFileTypes: true });
  for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
    const path = join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error(`${relative(root, path)} is a symbolic link.`);
    if (entry.isDirectory()) await findResultFiles(root, path, found);
    else if (RESULT_NAME.test(entry.name) && !entry.isFile()) throw new Error(`${relative(root, path)} is not a regular file.`);
    else if (RESULT_NAME.test(entry.name)) found.push(path);
  }
  return found;
}

async function readResult(root, path) {
  const label = relative(root, path);
  let value;
  try {
    value = JSON.parse(await readFile(path, 'utf8'));
  } catch (error) {
    throw new Error(`${label}: ${error.message}`);
  }
  const result = validateResult(value, label);
  if (basename(path) !== resultFileName(result)) throw new Error(`${label}: the file name does not match ${resultFileName(result)}.`);
  return { label, result };
}

export async function mergeResults({ input, output }) {
  const root = resolve(input);
  if (!(await lstat(root)).isDirectory()) throw new Error(`${input} is not a directory.`);
  const files = await findResultFiles(root, root, []);
  if (files.length === 0) throw new Error(`No benchmark result files were found under ${input}.`);
  const labels = new Map();
  const results = [];
  for (const path of files) {
    const { label, result } = await readResult(root, path);
    const key = resultKey(result);
    if (labels.has(key)) throw new Error(`${label} repeats ${key} from ${labels.get(key)}.`);
    labels.set(key, label);
    results.push(result);
  }
  results.sort(compareResults);
  const target = resolve(output);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, JSON.stringify({ results }, null, 2) + '\n', { flag: 'wx' });
  return { output: target, files: files.length };
}
