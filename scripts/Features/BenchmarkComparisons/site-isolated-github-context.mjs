import { dirname, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { constants } from 'node:fs';
import { open } from 'node:fs/promises';
import { parseBytes, absolutePath, existingPath } from './aggregate-files.mjs';
import { positive, shaPattern } from './isolated-github-contract.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';

const repositoryRoot = dirname(dirname(dirname(dirname(fileURLToPath(import.meta.url)))));
const captureOptions = new Set(['input', 'mode', 'site-revision', 'workflow-revision', 'requested-run']);

export function parseSiteIsolatedArguments(argv, allowed, required) {
  const values = {};
  for (const item of argv) {
    const matched = /^--([a-z-]+)=(.+)$/.exec(item);
    requireSite(matched !== null && allowed.has(matched[1]) && !Object.hasOwn(values, matched[1]));
    values[matched[1]] = matched[2];
  }
  requireSite(required.every(name => Object.hasOwn(values, name)));
  return values;
}

export function parseSiteCaptureArguments(argv) {
  const args = parseSiteIsolatedArguments(argv, captureOptions, ['input', 'mode', 'site-revision', 'workflow-revision']);
  requireSite(isAbsolute(args.input) && [SITE_GH.publish, SITE_GH.validate].includes(args.mode)
    && shaPattern.test(args['site-revision']) && shaPattern.test(args['workflow-revision']));
  if (Object.hasOwn(args, 'requested-run')) requireSite(args.mode === SITE_GH.validate && /^[1-9][0-9]*$/.test(args['requested-run']));
  return args;
}

export async function readSiteProducerEvent(eventPath) {
  absolutePath(eventPath);
  const original = await existingPath(eventPath, false);
  requireSite(original.size > 0 && original.size <= SITE_GH.eventBytes);
  const handle = await open(eventPath, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  try {
    const before = await handle.stat();
    requireSite(before.isFile() && before.ino === original.ino && before.dev === original.dev
      && before.size === original.size && before.mtimeMs === original.mtimeMs);
    const bytes = Buffer.alloc(before.size + 1);
    let count = 0;
    while (count < bytes.length) {
      const result = await handle.read(bytes, count, bytes.length - count, null);
      if (result.bytesRead === 0) break;
      count += result.bytesRead;
    }
    const after = await handle.stat();
    const retained = await existingPath(eventPath, false);
    requireSite(count === before.size && after.size === before.size && after.mtimeMs === before.mtimeMs
      && retained.ino === before.ino && retained.dev === before.dev && retained.size === before.size && retained.mtimeMs === before.mtimeMs);
    return validateSiteProducerEvent(parseBytes(bytes.subarray(0, count)));
  } finally { await handle.close(); }
}

export function validateSiteProducerEvent(event) {
  const run = event?.workflow_run;
  requireSite(event?.action === 'completed' && event.repository?.id === SITE_GH.repositoryId
    && event.repository.full_name === SITE_GH.repository && run?.repository?.id === SITE_GH.repositoryId
    && run.repository.full_name === SITE_GH.repository && run.head_repository?.id === SITE_GH.repositoryId
    && run.head_repository.full_name === SITE_GH.repository && run.head_branch === 'main'
    && run.name === 'Benchmarks' && run.path === '.github/workflows/benchmarks.yml'
    && run.status === 'completed' && SITE_GH.producerConclusions.includes(run.conclusion)
    && SITE_GH.producerEvents.includes(run.event) && shaPattern.test(run.head_sha ?? '')
    && positive(run.id) && positive(run.run_attempt));
  return { runId: run.id, attempt: run.run_attempt, sourceRevision: run.head_sha, event: run.event, conclusion: run.conclusion };
}

export async function createSiteIsolatedContext(environment, args, platform = process.platform) {
  requireSite(platform === 'linux' && environment.GITHUB_REPOSITORY === SITE_GH.repository && environment.GH_REPO === SITE_GH.repository
    && environment.GITHUB_REPOSITORY_ID === String(SITE_GH.repositoryId)
    && environment.GITHUB_ACTIONS === 'true' && environment.RUNNER_OS === 'Linux'
    && environment.GITHUB_WORKFLOW === SITE_GH.executor && environment.GITHUB_WORKFLOW_SHA === args['workflow-revision']
    && environment.GITHUB_WORKFLOW_REF === `${SITE_GH.repository}/${SITE_GH.executorPath}@${environment.GITHUB_REF}`
    && SITE_GH.executorJobs.includes(environment.GITHUB_JOB) && SITE_GH.executorEvents.includes(environment.GITHUB_EVENT_NAME)
    && shaPattern.test(environment.GITHUB_SHA ?? '') && typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0);
  requireSite(/^[1-9][0-9]*$/.test(environment.GITHUB_RUN_ID ?? '') && /^[1-9][0-9]*$/.test(environment.GITHUB_RUN_ATTEMPT ?? '')
    && positive(Number(environment.GITHUB_RUN_ID)) && positive(Number(environment.GITHUB_RUN_ATTEMPT))
    && typeof environment.GITHUB_WORKSPACE === 'string' && isAbsolute(environment.GITHUB_WORKSPACE));
  requireSite(environment.GITHUB_REF === 'refs/heads/main');
  if (args.mode === SITE_GH.publish) requireSite(args['requested-run'] === undefined);
  const trigger = environment.GITHUB_EVENT_NAME === 'workflow_run'
    ? await readSiteProducerEvent(environment.GITHUB_EVENT_PATH) : null;
  return { native: { workspace: repositoryRoot }, executor: { sourceRevision: environment.GITHUB_SHA,
    runId: environment.GITHUB_RUN_ID, attempt: environment.GITHUB_RUN_ATTEMPT, workflow: environment.GITHUB_WORKFLOW,
    event: environment.GITHUB_EVENT_NAME },
    source: { website: args['site-revision'], control: args['workflow-revision'] }, mode: args.mode, requestedRun: args['requested-run'] ?? null,
    trigger, producer: null };
}
