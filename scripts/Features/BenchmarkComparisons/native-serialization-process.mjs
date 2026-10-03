import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { NATIVE, requireNative } from './native-serialization-contract.mjs';
import { collectFacts, hashObject } from './native-serialization-files.mjs';

const execute = promisify(execFile);

export async function command(file, args, maxBuffer = 4 * 1024 * 1024) {
  const result = await execute(file, args, { encoding: 'utf8', timeout: 60000, maxBuffer, windowsHide: true });
  return result.stdout;
}

export async function sourceFacts(environment) {
  requireNative((await command('git', ['rev-parse', 'HEAD'])).trim() === environment.GITHUB_SHA, 'source.revision');
  requireNative((await command('git', ['status', '--porcelain', '--untracked-files=no'])).trim() === '', 'source.dirty');
  const paths = (await command('git', ['ls-files', '-z'])).split('\0').filter(Boolean);
  requireNative(paths.length > 0 && paths.includes(NATIVE.workflowPath), 'source.inventory');
  const files = await collectFacts(process.cwd(), paths, paths);
  return { sha256: hashObject(files), files };
}

export async function githubJson(route) {
  return JSON.parse(await command('gh', ['api', route, '--method', 'GET', '-H', 'Accept: application/vnd.github+json',
    '-H', 'X-GitHub-Api-Version: 2022-11-28']));
}
