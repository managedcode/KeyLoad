import { lstat, open, readFile, appendFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolveReleaseVersion } from './release-version.mjs';

const MAX_INPUT_BYTES = 8 * 1024 * 1024;
const INPUT_KEYS = Object.freeze(['baseVersion', 'sourceRevision', 'runId', 'utcTimestamp', 'tags', 'dailyRuns', 'reservation']);
const ENV_VALUES = Object.freeze([
  ['RELEASE_VERSION', 'version'], ['RELEASE_TAG', 'tag'],
  ['RELEASE_ASSEMBLY_VERSION', 'assemblyVersion'], ['RELEASE_FILE_VERSION', 'fileVersion'],
  ['RELEASE_PACKAGE_VERSION', 'packageVersion'],
]);
const OUTPUT_VALUES = Object.freeze([
  ['version', 'version'], ['tag', 'tag'], ['assembly_version', 'assemblyVersion'], ['file_version', 'fileVersion'],
  ['package_version', 'packageVersion'],
]);

function fail(message) { throw new Error(message); }

function parseArguments(args) {
  const values = new Map();
  for (const argument of args) {
    const match = /^--(input|output)=(.+)$/.exec(argument);
    if (!match || values.has(match[1])) fail('Use one --input=<file> and one --output=<file>.');
    values.set(match[1], path.resolve(match[2]));
  }
  if (values.size !== 2) fail('Use one --input=<file> and one --output=<file>.');
  return { input: values.get('input'), output: values.get('output') };
}

async function readInput(file) {
  const info = await lstat(file);
  if (!info.isFile() || info.isSymbolicLink() || info.size > MAX_INPUT_BYTES) fail('Input JSON is missing, linked, or over its size limit.');
  const value = JSON.parse(await readFile(file, 'utf8'));
  const allowed = Object.keys(value ?? {});
  if (value === null || typeof value !== 'object' || Array.isArray(value) ||
      allowed.some(key => !INPUT_KEYS.includes(key)) || INPUT_KEYS.slice(0, 6).some(key => !Object.hasOwn(value, key))) {
    fail('Input JSON does not match the release version contract.');
  }
  return value;
}

function canonical(value) {
  return JSON.stringify(Object.fromEntries(Object.entries(value).sort(([left], [right]) => left.localeCompare(right))));
}

async function writeReservation(file, reservation) {
  const content = `${JSON.stringify(reservation, null, 2)}\n`;
  try {
    const handle = await open(file, 'wx', 0o600);
    try { await handle.writeFile(content, 'utf8'); }
    finally { await handle.close(); }
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    const info = await lstat(file);
    if (!info.isFile() || info.isSymbolicLink() || info.size > 16 * 1024) fail('Existing reservation output is invalid.');
    let existing;
    try { existing = JSON.parse(await readFile(file, 'utf8')); }
    catch { fail('Existing reservation output is invalid JSON.'); }
    if (existing === null || typeof existing !== 'object' || Array.isArray(existing) ||
        canonical(existing) !== canonical(reservation)) fail('Refusing to overwrite a different release reservation.');
  }
}

async function appendValues(file, entries, reservation) {
  if (!file) return;
  const text = entries.map(([name, key]) => `${name}=${reservation[key]}\n`).join('');
  await appendFile(file, text, { encoding: 'utf8', mode: 0o600 });
}

export async function runReleaseVersionCli(args = process.argv.slice(2), environment = process.env) {
  const { input, output } = parseArguments(args);
  const reservation = resolveReleaseVersion(await readInput(input));
  await writeReservation(output, reservation);
  await appendValues(environment.GITHUB_ENV, ENV_VALUES, reservation);
  await appendValues(environment.GITHUB_OUTPUT, OUTPUT_VALUES, reservation);
  return reservation;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runReleaseVersionCli().catch(error => {
    const code = typeof error.code === 'string' ? error.code : 'E_RELEASE_CLI';
    process.stderr.write(`${code}: ${error.message}\n`);
    process.exitCode = 1;
  });
}
