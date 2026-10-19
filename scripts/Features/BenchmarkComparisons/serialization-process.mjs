import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { EXECUTION, runtimeRequire } from './serialization-contract.mjs';
import { collectFacts, hashObject } from './serialization-files.mjs';

const execute = promisify(execFile);

export async function command(file, args, maxBuffer = 4 * 1024 * 1024, signal) {
  signal?.throwIfAborted();
  const execution = execute(file, args, { encoding: 'utf8', timeout: 60000, maxBuffer, windowsHide: true, signal });
  const closed = new Promise(resolve => execution.child.once('close', resolve));
  try { return (await execution).stdout; }
  finally { await closed; }
}

export async function sourceFacts(environment) {
  runtimeRequire((await command('git', ['rev-parse', 'HEAD'])).trim() === environment.GITHUB_SHA, 'source.revision');
  runtimeRequire((await command('git', ['status', '--porcelain', '--untracked-files=no'])).trim() === '', 'source.dirty');
  const paths = (await command('git', ['ls-files', '-z'])).split('\0').filter(Boolean);
  runtimeRequire(paths.length > 0 && paths.includes(EXECUTION.workflowPath), 'source.inventory');
  const files = await collectFacts(process.cwd(), paths, paths);
  return { sha256: hashObject(files), files };
}

export async function githubJson(route, signal) {
  return JSON.parse(await command('gh', ['api', route, '--method', 'GET', '-H', 'Accept: application/vnd.github+json',
    '-H', 'X-GitHub-Api-Version: 2022-11-28'], undefined, signal));
}
