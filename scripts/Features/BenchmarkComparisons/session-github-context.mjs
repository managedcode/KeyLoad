import { createRunContext } from './source-context.mjs';
import { GH } from './isolated-github-contract.mjs';
import { SESSION, requireSession, sessionRows, validateSessionCohort } from './session-contract.mjs';

export function createSessionContext(environment, platform) {
  const native = createRunContext(environment, platform);
  requireSession(environment.GITHUB_ACTIONS === 'true' && environment.RUNNER_OS === 'Linux'
    && environment.GITHUB_REPOSITORY_ID === '477801965' && environment.GITHUB_WORKFLOW === SESSION.workflow && native.repository === SESSION.repository
    && native.ref === SESSION.ref && typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0
    && environment.GITHUB_WORKFLOW_REF === `${SESSION.repository}/${GH.workflowPath}@${SESSION.ref}`);
  const cohort = validateSessionCohort({ sourceRevision: native.sourceSha, runId: Number(native.runId),
    attempt: Number(native.runAttempt), repository: native.repository, ref: native.ref,
    workflow: SESSION.workflow, profile: SESSION.profile });
  return { native, cohort };
}
export function selectSession(environment) {
  const selected = sessionRows().filter(row => row.id === environment.KEYLOAD_BENCHMARK_SESSION_ID);
  requireSession(selected.length === 1);
  const row = selected[0];
  requireSession(environment.KEYLOAD_COMPARISON_JOB_NAME === row.jobName && environment.Benchmarks__Target === row.target
    && environment.Benchmarks__NodeCount === String(row.nodeCount) && environment.Benchmarks__Session === 'true');
  return row;
}
