import { dirname, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { positive, shaPattern } from './isolated-github-contract.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';

const repositoryRoot = dirname(dirname(dirname(dirname(fileURLToPath(import.meta.url)))));
const captureOptions = new Set(['input', 'mode', 'site-revision', 'workflow-revision', 'requested-run', 'optional']);

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
  if (Object.hasOwn(args, 'optional')) requireSite(args.optional === 'true' && args.mode === SITE_GH.publish);
  if (Object.hasOwn(args, 'requested-run')) requireSite(args.mode === SITE_GH.validate && /^[1-9][0-9]*$/.test(args['requested-run']));
  return args;
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
  return { native: { workspace: repositoryRoot }, executor: { sourceRevision: environment.GITHUB_SHA,
    runId: environment.GITHUB_RUN_ID, attempt: environment.GITHUB_RUN_ATTEMPT, workflow: environment.GITHUB_WORKFLOW,
    event: environment.GITHUB_EVENT_NAME },
    source: { website: args['site-revision'], control: args['workflow-revision'] }, mode: args.mode, requestedRun: args['requested-run'] ?? null,
    optional: args.optional === 'true', trigger: null, producer: null };
}
