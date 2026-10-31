import { GH } from './isolated-github-contract.mjs';
import { SESSION, requireSession, sessionRows, validateSessionCohort } from './session-contract.mjs';
import { selectSessionEvidence } from './session-github-selection.mjs';

export function validateSessionProducer(run, workflow, mode = 'validate') {
  requireSession(['publish', 'validate'].includes(mode));
  requireSession(workflow?.path === GH.workflowPath && workflow.name === SESSION.workflow && workflow.state === 'active'
    && Number.isSafeInteger(workflow.id) && workflow.id > 0 && run?.workflow_id === workflow.id
    && run.path === GH.workflowPath && run.name === SESSION.workflow && run.head_branch === 'main'
    && ['push', 'workflow_dispatch'].includes(run.event) && run.repository?.id === 477801965 && run.head_repository?.id === 477801965
    && run.repository?.full_name === SESSION.repository && run.head_repository?.full_name === SESSION.repository
    && run.status === 'completed' && run.conclusion === 'success');
  return validateSessionCohort({ sourceRevision: run.head_sha, runId: run.id, attempt: run.run_attempt,
    repository: SESSION.repository, ref: SESSION.ref, workflow: SESSION.workflow, profile: SESSION.profile });
}
export function selectSessionSiteProducer(capture, workflow, mode = 'validate') {
  const cohort = validateSessionProducer(capture.run, workflow, mode);
  const available = sessionRows().some(row => capture.jobs.some(job => job.name === row.jobName
    && job.status === 'completed' && job.conclusion === 'success'));
  if (!available) return null;
  const execution = selectSessionEvidence(capture, cohort);
  requireSession(execution.workers.length === SESSION.workers);
  return { cohort, execution };
}

// Publication requires every planned worker from the same successful full run.
export function requirePublishableSessionMeasurements(aggregate) {
  const caseIds = aggregate.plan.caseIds;
  requireSession(Array.isArray(aggregate.workers) && aggregate.workers.length === SESSION.workers);
  for (const worker of aggregate.workers) {
    requireSession(worker.job.conclusion === 'success' && ['success','unavailable'].includes(worker.session.outcome)
      && ['passed','notApplicable'].includes(worker.qualification.status)
      && worker.execution.length === caseIds.length && worker.execution.every(test => test.outcome === 'Passed')
      && worker.reports.length === caseIds.length
      && worker.reports.every((report,index) => report.caseId === caseIds[index]
        && ['measured','unavailable'].includes(report.status)));
    if (worker.reports.some(report => report.status === 'measured')) {
      const profile = worker.session.targetProfile;
      requireSession(typeof worker.resourceQualified === 'boolean' && profile !== null
        && [profile.version,profile.topology,profile.cluster.state].every(value =>
          typeof value === 'string' && !/unverified|unobserved|unknown|not recorded/i.test(value)));
    }
  }
  return aggregate;
}
