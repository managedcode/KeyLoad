import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SESSION, requireSession, sessionPositive } from './session-contract.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
export function parseSessionSiteArguments(argv, names, required) {
  const result = {};
  for (const argument of argv) {
    const match = /^--([a-z-]+)=(.+)$/.exec(argument);
    requireSession(match !== null && names.includes(match[1]) && !Object.hasOwn(result, match[1]));
    result[match[1]] = match[2];
  }
  requireSession(required.every(name => Object.hasOwn(result, name)));
  return result;
}
export function sessionSiteContext(environment, args, platform = process.platform) {
  requireSession(platform === 'linux' && environment.GITHUB_ACTIONS === 'true' && environment.RUNNER_OS === 'Linux'
    && environment.GITHUB_REPOSITORY === SESSION.repository && environment.GH_REPO === SESSION.repository
    && environment.GITHUB_REPOSITORY_ID === '477801965' && environment.GITHUB_WORKFLOW === 'Website'
    && environment.GITHUB_REF === SESSION.ref && ['qualify', 'deploy'].includes(environment.GITHUB_JOB)
    && environment.GITHUB_EVENT_NAME === 'workflow_dispatch'
    && environment.GITHUB_WORKFLOW_REF === `${SESSION.repository}/.github/workflows/website.yml@${SESSION.ref}`
    && environment.GITHUB_WORKFLOW_SHA === args['workflow-revision'] && /^[a-f0-9]{40}$/.test(environment.GITHUB_SHA ?? '')
    && /^[a-f0-9]{40}$/.test(args['site-revision'] ?? '') && /^[a-f0-9]{40}$/.test(args['workflow-revision'] ?? '')
    && sessionPositive(Number(environment.GITHUB_RUN_ID)) && sessionPositive(Number(environment.GITHUB_RUN_ATTEMPT))
    && typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0
    && typeof environment.GITHUB_WORKSPACE === 'string' && path.isAbsolute(environment.GITHUB_WORKSPACE)
    && path.isAbsolute(args.input ?? '') && ['publish', 'validate'].includes(args.mode)
    && (args.optional === undefined || args.optional === 'true' && args.mode === 'publish')
    && (args['requested-run'] === undefined || /^[1-9]\d{0,19}$/.test(args['requested-run'])));
  return { execution: { workspace: root }, source: { website: args['site-revision'], control: args['workflow-revision'] },
    mode: args.mode, optional: args.optional === 'true', requestedRun: args['requested-run'] ?? null };
}
