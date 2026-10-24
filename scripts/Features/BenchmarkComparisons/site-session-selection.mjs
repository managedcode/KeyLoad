import { selectDocumentFunctionalGate } from './document-functional-gate.mjs';
import { GH } from './isolated-github-contract.mjs';
import { uniqueNamed, validateArtifact, validateSuccessfulJob } from './isolated-github-validation.mjs';
import { SESSION, requireSession, validateSessionCohort } from './session-contract.mjs';
import { selectSessionEvidence } from './session-github-selection.mjs';

export const SESSION_SITE = Object.freeze({ provider: 'comparison-session-provider-evidence', suiteBytes: 18_253_611_008,
  providerBytes: 134_217_728, aggregateSteps: ['Check complete session accounting', 'Save combined benchmark results', 'Save GitHub result verification'] });
export function validateSessionProducer(run, workflow, mode = 'validate') {
  requireSession(['publish', 'validate'].includes(mode) && (mode !== 'publish' || run?.conclusion === 'success'));
  requireSession(workflow?.path === GH.workflowPath && workflow.name === SESSION.workflow && workflow.state === 'active'
    && Number.isSafeInteger(workflow.id) && workflow.id > 0 && run?.workflow_id === workflow.id
    && run.path === GH.workflowPath && run.name === SESSION.workflow && run.head_branch === 'main'
    && ['push', 'workflow_dispatch'].includes(run.event) && run.repository?.id === 477801965 && run.head_repository?.id === 477801965
    && run.repository?.full_name === SESSION.repository && run.head_repository?.full_name === SESSION.repository
    && run.status === 'completed' && ['success', 'failure'].includes(run.conclusion));
  return validateSessionCohort({ sourceRevision: run.head_sha, runId: run.id, attempt: run.run_attempt,
    repository: SESSION.repository, ref: SESSION.ref, workflow: SESSION.workflow, profile: SESSION.profile });
}
export function selectSessionSiteProducer(capture, workflow, mode = 'validate') {
  const cohort = validateSessionProducer(capture.run, workflow, mode);
  const named = capture.jobs.filter(job => job.name === SESSION.aggregateJob);
  requireSession(named.length <= 1);
  if (named.length === 0 || named[0].status !== 'completed' || named[0].conclusion !== 'success') {
    requireSession(!capture.artifacts.some(artifact => artifact.name === SESSION.aggregateArtifact));
    return null;
  }
  // An older producer with no session schema is not a supported fallback input.
  if (!capture.artifacts.some(artifact => artifact.name === SESSION.aggregateArtifact)) return null;
  const job = validateSuccessfulJob(uniqueNamed(capture.jobs, SESSION.aggregateJob), cohort,
    SESSION.aggregateJob, SESSION_SITE.aggregateSteps);
  const suite = validateArtifact(uniqueNamed(capture.artifacts, SESSION.aggregateArtifact), capture.run,
    job, SESSION.aggregateArtifact, SESSION_SITE.suiteBytes);
  const provider = validateArtifact(uniqueNamed(capture.artifacts, SESSION_SITE.provider), capture.run,
    job, SESSION_SITE.provider, SESSION_SITE.providerBytes);
  return { cohort, job, suite, provider, functional:selectDocumentFunctionalGate(capture,cohort), execution: selectSessionEvidence(capture, cohort) };
}

// Called after complete aggregate validation; failed originals remain reviewable in validate mode only.
export function requirePublishableSessionMeasurements(aggregate) {
  requireSession(Array.isArray(aggregate.workers) && aggregate.workers.length === SESSION.workers);
  for (const worker of aggregate.workers) {
    requireSession(worker.job.conclusion === 'success' && ['success','unavailable'].includes(worker.session.outcome)
      && ['passed','notApplicable'].includes(worker.qualification.status)
      && worker.execution.length === SESSION.caseIds.length && worker.execution.every(test => test.outcome === 'Passed')
      && worker.reports.length === SESSION.caseIds.length
      && worker.reports.every((report,index) => report.caseId === SESSION.caseIds[index]
        && ['measured','unavailable'].includes(report.status)));
    if (worker.reports.some(report => report.status === 'measured')) {
      const profile = worker.session.targetProfile;
      requireSession(worker.resourceQualified && profile !== null
        && [profile.version,profile.topology,profile.cluster.state].every(value =>
          typeof value === 'string' && !/unverified|unobserved|unknown|not recorded/i.test(value)));
    }
  }
  return aggregate;
}
