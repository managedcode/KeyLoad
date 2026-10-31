import { SESSION, exactSession as exact, requireSession as require, sessionRows, sessionHash, validateSessionCohort } from './session-contract.mjs';
import { validateSessionPlan } from './session-plan-contract.mjs';
import { validateSessionCase, validateSessionEnvelope, validateSessionQualification, validateSessionProfileAccounting } from './session-report-validation.mjs';

export function validateSessionAggregate(value) {
  require(exact(value, ['schemaVersion', 'cohort', 'plan', 'preparation', 'workers'])
    && value.schemaVersion === SESSION.schema);
  validateSessionCohort(value.cohort);
  validateSessionPlan(value.plan, value.plan.caseIds);
  const caseIds = value.plan.caseIds;
  require(exact(value.preparation, ['job', 'compiledArtifact', 'runtimeIdentity'])
    && Array.isArray(value.workers) && value.workers.length > 0 && value.workers.length <= SESSION.workers);
  const jobs = new Set(), artifacts = new Set(), sessions = new Set();
  const rows = sessionRows();
  let previous = -1;
  for (const worker of value.workers) {
    const index = rows.findIndex(row => row.id === worker.id);
    require(index > previous);
    previous = index;
    const row = rows[index];
    require(exact(worker, ['id', 'target', 'nodeCount', 'job', 'artifact', 'sessionSha256', 'session', 'reports', 'execution', 'qualification', 'resourceQualified', 'stateSha256'])
      && worker.id === row.id && worker.target === row.target && worker.nodeCount === row.nodeCount
      && worker.job?.conclusion === 'success' && worker.artifact?.expired === false
      && /^sha256:[a-f0-9]{64}$/.test(worker.artifact?.digest ?? '')
      && worker.job?.name === row.jobName && worker.artifact?.name === row.artifactName
      && sessionHash(worker.stateSha256) && typeof worker.resourceQualified === 'boolean' && sessionHash(worker.sessionSha256) && !jobs.has(worker.job.id) && !artifacts.has(worker.artifact.id));
    jobs.add(worker.job.id); artifacts.add(worker.artifact.id);
    const session = validateSessionEnvelope(worker.session, row, value.cohort, worker.job.id, null, caseIds);
    require(!sessions.has(session.sessionId) && Array.isArray(worker.reports) && worker.reports.length === caseIds.length
      && Array.isArray(worker.execution) && worker.execution.length === caseIds.length);
    require(session.outcome === 'failure' ? worker.job.conclusion === 'failure' : worker.job.conclusion === 'success');
    validateSessionQualification(worker.qualification,session,worker.execution);
    sessions.add(session.sessionId);
    for (const [caseIndex, report] of worker.reports.entries()) require(report.caseId === caseIds[caseIndex]
      && session.cases[caseIndex].id === caseIds[caseIndex] && validateSessionCase(report, caseIds[caseIndex], false));
    validateSessionProfileAccounting(session,worker.reports,worker.execution,worker.qualification);
  }
  return value;
}
