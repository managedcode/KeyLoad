import { constants } from 'node:fs';
import { lstat, open, writeFile } from 'node:fs/promises';
import { dirname, parse, resolve } from 'node:path';
import { requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { createDatabaseMatrices } from './isolated-preflight.mjs';
import { createCompositePlan, createScaledPlans } from './scaled-isolated-plan.mjs';
import { createVectorPlans } from './vector-isolated-plan.mjs';

const options = new Set(['--output', '--scale-output', '--vector-output', '--composite-output', '--github-output']);
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

export async function runIsolatedPlanCli(arguments_, plan, scaledPlans = createScaledPlans()) {
  const parsed = parseArguments(arguments_);
  const output = parsed['--output'];
  const scaleOutput = parsed['--scale-output'];
  const vectorOutput = parsed['--vector-output'];
  const compositeOutput = parsed['--composite-output'];
  if (output !== undefined) await requirePlainParents(output);
  if (scaleOutput !== undefined) await requirePlainParents(scaleOutput);
  if (vectorOutput !== undefined) await requirePlainParents(vectorOutput);
  if (compositeOutput !== undefined) await requirePlainParents(compositeOutput);
  const text = JSON.stringify(plan, null, 2) + '\n';
  const scaleText = JSON.stringify(scaledPlans, null, 2) + '\n';
  const vectorPlans = createVectorPlans();
  const vectorText = JSON.stringify(vectorPlans, null, 2) + '\n';
  const compositeText = JSON.stringify(createCompositePlan(plan, scaledPlans, vectorPlans), null, 2) + '\n';
  const github = await openGithubOutput(parsed['--github-output']);
  try {
    if (output !== undefined) await writeFile(output, text, { encoding: 'utf8', flag: 'wx' });
    if (scaleOutput !== undefined) await writeFile(scaleOutput, scaleText, { encoding: 'utf8', flag: 'wx' });
    if (vectorOutput !== undefined) await writeFile(vectorOutput, vectorText, { encoding: 'utf8', flag: 'wx' });
    if (compositeOutput !== undefined) await writeFile(compositeOutput, compositeText, { encoding: 'utf8', flag: 'wx' });
    if (github !== null) {
      await github.writeFile('database_matrices=' + JSON.stringify(createDatabaseMatrices(plan, scaledPlans, vectorPlans)) + '\n', 'utf8');
    }
    process.stdout.write(text);
  } finally {
    await github?.close();
  }
}
