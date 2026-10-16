import { SESSION, exactSession as exact, requireSession as require, sessionRows, sessionHash, validateSessionCohort } from './session-contract.mjs';
import { validateSessionPlan } from './session-plan-contract.mjs';
import { validateSessionCase, validateSessionEnvelope, validateSessionQualification, validateSessionProfileAccounting } from './session-report-validation.mjs';

export function validateSessionAggregate(value) {
  require(exact(value, ['schemaVersion', 'cohort', 'plan', 'images', 'preparation', 'workers'])
    && value.schemaVersion === SESSION.schema);
  validateSessionCohort(value.cohort);
  validateSessionPlan(value.plan);
  require(exact(value.images, ['server', 'loadGenerator']) && exact(value.preparation, ['job', 'artifact', 'compiledArtifact', 'runtimeIdentity', 'functional'])
    && Array.isArray(value.workers) && value.workers.length === SESSION.workers);
  const jobs = new Set(), artifacts = new Set(), sessions = new Set();
  for (const [index, worker] of value.workers.entries()) {
    const row = sessionRows()[index];
    require(exact(worker, ['id', 'target', 'nodeCount', 'job', 'artifact', 'sessionSha256', 'session', 'reports', 'native', 'qualification', 'resourceQualified', 'stateSha256'])
      && worker.id === row.id && worker.target === row.target && worker.nodeCount === row.nodeCount
      && ['success', 'failure'].includes(worker.job?.conclusion) && worker.artifact?.expired === false
      && /^sha256:[a-f0-9]{64}$/.test(worker.artifact?.digest ?? '')
      && worker.job?.name === row.jobName && worker.artifact?.name === row.artifactName
      && (worker.stateSha256 === null || sessionHash(worker.stateSha256)) && typeof worker.resourceQualified === 'boolean' && sessionHash(worker.sessionSha256) && !jobs.has(worker.job.id) && !artifacts.has(worker.artifact.id));
    jobs.add(worker.job.id); artifacts.add(worker.artifact.id);
    const session = validateSessionEnvelope(worker.session, row, value.cohort, worker.job.id);
    require(!sessions.has(session.sessionId) && Array.isArray(worker.reports) && worker.reports.length === SESSION.caseIds.length
      && Array.isArray(worker.native) && worker.native.length === SESSION.caseIds.length
      && session.images.runner === value.images.loadGenerator
      && (row.target !== 'KeyLoad' || session.images.server === value.images.server));
    require(session.outcome === 'failure' ? worker.job.conclusion === 'failure' : worker.job.conclusion === 'success');
    validateSessionQualification(worker.qualification,session,worker.native);
    sessions.add(session.sessionId);
    for (const [caseIndex, report] of worker.reports.entries()) require(report.caseId === SESSION.caseIds[caseIndex]
      && validateSessionCase(report, SESSION.caseIds[caseIndex], false));
    validateSessionProfileAccounting(session,worker.reports,worker.native,worker.qualification);
  }
  return value;
}
