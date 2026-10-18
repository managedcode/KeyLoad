import { RUNTIME_IDENTITY } from './session-runtime-identity.mjs';
import { timestamp } from './isolated-github-contract.mjs';
import { uniqueNamed, validateArtifact, validateCompletedJob, validateSuccessfulJob } from './isolated-github-validation.mjs';
import { SESSION, requireSession, sessionRows, validateSessionCohort } from './session-contract.mjs';

export function validateSessionJob(job, cohort, row) {
  return validateCompletedJob(job, cohort, row.jobName, SESSION.workerSteps, ['success', 'failure']);
}
export function selectSessionEvidence(capture, cohort) {
  validateSessionCohort(cohort);
  const { jobs, artifacts, run } = capture;
  const rows = sessionRows();
  requireSession(Array.isArray(jobs) && Array.isArray(artifacts)
    && jobs.filter(job => job.name?.includes(' / Session / ')).length === SESSION.workers
    && artifacts.filter(artifact => rows.some(row => row.artifactName === artifact.name)).length === SESSION.workers
    && !artifacts.some(artifact => /^comparison-(?:worker|open-loop-worker|document-worker)-/.test(artifact.name ?? ''))
    && artifacts.filter(artifact => artifact.name?.startsWith(SESSION.artifactPrefix))
      .every(artifact => rows.some(row => row.artifactName === artifact.name)
        || [SESSION.aggregateArtifact,'comparison-session-provider-evidence'].includes(artifact.name)));
  const selected = rows.map(row => {
    const job = validateSessionJob(uniqueNamed(jobs, row.jobName), cohort, row);
    requireSession(timestamp(job.started_at) >= timestamp(run.run_started_at));
    const artifact = validateArtifact(uniqueNamed(artifacts, row.artifactName), run, job, row.artifactName, SESSION.archiveBytes);
    return { row, job, artifact };
  });
  requireSession(new Set(selected.map(item => item.job.id)).size === SESSION.workers
    && new Set(selected.map(item => item.artifact.id)).size === SESSION.workers
    && selected.reduce((sum, item) => sum + item.artifact.size_in_bytes, 0) <= SESSION.allArchiveBytes);
  const preparation = validateSuccessfulJob(uniqueNamed(jobs, SESSION.prepareJob), cohort, SESSION.prepareJob,
    SESSION.prepareSteps);
  const compiledArtifact = validateArtifact(uniqueNamed(artifacts, 'comparison-compiled-test-bundle'), run,
    preparation, 'comparison-compiled-test-bundle', 6_576_668_672);
  const runtimeArtifact = validateArtifact(uniqueNamed(artifacts, RUNTIME_IDENTITY.artifact), run, preparation,
    RUNTIME_IDENTITY.artifact, RUNTIME_IDENTITY.maximumBytes);
  return { runtimeArtifact, workers: selected, preparation, compiledArtifact };
}
