import { createRunContext } from './source-context.mjs';
import { GH } from './isolated-github-contract.mjs';
import { SESSION, requireSession, sessionRows, validateSessionCohort } from './session-contract.mjs';

export function createSessionContext(environment, platform) {
  const execution = createRunContext(environment, platform);
  requireSession(environment.GITHUB_ACTIONS === 'true' && environment.RUNNER_OS === 'Linux'
    && environment.GITHUB_REPOSITORY_ID === '477801965' && environment.GITHUB_WORKFLOW === SESSION.workflow && execution.repository === SESSION.repository
    && execution.ref === SESSION.ref && typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0
    && environment.GITHUB_WORKFLOW_REF === `${SESSION.repository}/${GH.workflowPath}@${SESSION.ref}`);
  const cohort = validateSessionCohort({ sourceRevision: execution.sourceSha, runId: Number(execution.runId),
    attempt: Number(execution.runAttempt), repository: execution.repository, ref: execution.ref,
    workflow: SESSION.workflow, profile: SESSION.profile });
  return { execution, cohort };
}
export function selectSession(environment) {
  const selected = sessionRows().filter(row => row.id === environment.KEYLOAD_BENCHMARK_SESSION_ID);
  requireSession(selected.length === 1);
  const row = selected[0];
  requireSession(environment.KEYLOAD_COMPARISON_JOB_NAME === row.jobName && environment.Benchmarks__Target === row.target
    && environment.Benchmarks__NodeCount === String(row.nodeCount) && environment.Benchmarks__Session === 'true');
  return row;
}
