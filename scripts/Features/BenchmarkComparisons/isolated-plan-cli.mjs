import { constants } from 'node:fs';
import { lstat, open, writeFile } from 'node:fs/promises';
import { dirname, parse, resolve } from 'node:path';
import { requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { createPreflightMatrix } from './isolated-preflight.mjs';

const options = new Set(['--output', '--github-output']);
const maximumPathLength = 4096;

function parseArguments(arguments_) {
  const result = {};
  for (const argument of arguments_) {
    const separator = argument.indexOf('=');
    const key = argument.slice(0, separator);
    const value = argument.slice(separator + 1);
    requireIsolatedPlan(separator > 0 && options.has(key) && !Object.hasOwn(result, key));
    requireIsolatedPlan(value.length > 0 && value.length <= maximumPathLength
      && !/[\u0000-\u001f\u007f]/u.test(value));
    result[key] = resolve(value);
  }
  return result;
}

async function requirePlainParents(path) {
  const root = parse(path).root;
  for (let current = dirname(path); current !== root; current = dirname(current)) {
    const metadata = await lstat(current);
    requireIsolatedPlan(metadata.isDirectory() && !metadata.isSymbolicLink());
  }
}

async function openGithubOutput(path) {
  if (path === undefined) return null;
  await requirePlainParents(path);
  const before = await lstat(path);
  requireIsolatedPlan(before.isFile() && !before.isSymbolicLink());
  const handle = await open(path, constants.O_WRONLY | constants.O_APPEND | (constants.O_NOFOLLOW ?? 0));
  try {
    const opened = await handle.stat();
    requireIsolatedPlan(opened.isFile() && opened.dev === before.dev && opened.ino === before.ino);
    return handle;
  } catch (error) {
    await handle.close();
    throw error;
  }
}

export async function runIsolatedPlanCli(arguments_, plan) {
  const parsed = parseArguments(arguments_);
  const output = parsed['--output'];
  if (output !== undefined) await requirePlainParents(output);
  const text = JSON.stringify(plan, null, 2) + '\n';
  const github = await openGithubOutput(parsed['--github-output']);
  try {
    if (output !== undefined) await writeFile(output, text, { encoding: 'utf8', flag: 'wx' });
    if (github !== null) {
      await github.writeFile('crud_matrix=' + JSON.stringify(plan.matrices.crud) + '\n'
        + 'specialized_matrix=' + JSON.stringify(plan.matrices.specialized) + '\n'
        + 'preflight_matrix=' + JSON.stringify(createPreflightMatrix(plan)) + '\n', 'utf8');
    }
    process.stdout.write(text);
  } finally {
    await github?.close();
  }
}
