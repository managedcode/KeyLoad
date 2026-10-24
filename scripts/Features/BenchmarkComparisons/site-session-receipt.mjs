import { SESSION, exactSession as exact, requireSession as require, sessionHash, sessionPath,
  sessionRows, sessionPositive, sessionCount, validateSessionCohort } from './session-contract.mjs';

const common = ['schemaVersion', 'state', 'mode', 'publishEligible', 'source', 'producer', 'metadataFiles'];
const selected = ['repository', 'workflow', 'run', 'cohort', 'aggregateJob', 'artifacts', 'workers', 'preparation', 'runtimeIdentity',
  'suiteFiles', 'workerArchives', 'archives', 'inputFiles'];
export function validateSessionSiteReceipt(value) {
  require(value?.schemaVersion === SESSION.schema && ['publish', 'validate'].includes(value.mode)
    && value.publishEligible === (value.mode === 'publish') && Array.isArray(value.metadataFiles) && value.metadataFiles.length > 0);
  const absent = value.state === 'unavailable';
  require(exact(value, absent ? common : [...common, ...selected]) && exact(value.source,
    absent ? ['website', 'control'] : ['website', 'control', 'measured'])
    && Object.values(value.source).every(hash => /^[a-f0-9]{40}$/.test(hash ?? '')));
  validateFiles(value.metadataFiles, true);
  if (absent) { require(value.producer === null); return value; }
  require(['metadata_verified', 'archive_verified'].includes(value.state));
  validateSessionCohort(value.cohort);
  require(value.source.measured === value.cohort.sourceRevision && value.run?.id === value.cohort.runId
    && value.run.attempt === value.cohort.attempt
    && value.run.url === `https://github.com/${SESSION.repository}/actions/runs/${value.cohort.runId}`
    && value.repository?.id === 477801965 && value.repository.fullName === SESSION.repository
    && value.workflow?.path === '.github/workflows/benchmarks.yml' && sessionPositive(value.workflow.id));
  require(Array.isArray(value.workers) && value.workers.length === SESSION.workers
    && Array.isArray(value.workerArchives) && value.workerArchives.length === SESSION.workers);
  for (const artifact of Object.values(value.artifacts)) validateArtifactIdentity(artifact);
  validateJobProof(value.aggregateJob, value.cohort, ['Check complete session accounting', 'Save combined benchmark results', 'Save GitHub result verification'], false);
  require(exact(value.preparation, ['job', 'compiledArtifact']) && value.preparation.job?.name === SESSION.prepareJob
    && value.preparation.compiledArtifact?.name === 'comparison-compiled-test-bundle');
  for (const artifact of [value.preparation.compiledArtifact, value.runtimeIdentity.artifact]) validateArtifactIdentity(artifact);
  validateJobProof(value.preparation.job, value.cohort, SESSION.prepareSteps, false);
  const seenJobs = new Set(), seenArtifacts = new Set();
  for (const [index, row] of sessionRows().entries()) {
    const worker = value.workers[index];
    require(worker?.id === row.id && worker.target === row.target && worker.nodeCount === row.nodeCount
      && worker.job?.name === row.jobName && worker.artifact?.name === row.artifactName
      && sessionPositive(worker.job.id) && sessionPositive(worker.artifact.id)
      && !seenJobs.has(worker.job.id) && !seenArtifacts.has(worker.artifact.id));
    validateJobProof(worker.job, value.cohort, SESSION.workerSteps, true);
    validateArtifactIdentity(worker.artifact);
    seenJobs.add(worker.job.id); seenArtifacts.add(worker.artifact.id);
    const archive = value.workerArchives[index];
    require(exact(archive, ['id', 'path', 'sha256', 'bytes']) && archive.id === row.id
      && archive.path === `archives/${row.artifactName}.zip` && sessionHash(archive.sha256)
      && `sha256:${archive.sha256}` === worker.artifact.digest && sessionPositive(archive.bytes)
      && archive.bytes === worker.artifact.sizeInBytes);
  }
  require(Array.isArray(value.suiteFiles) && value.suiteFiles.length >= SESSION.workers * (SESSION.caseIds.length + 2) + 1);
  const paths = new Set();
  for (const file of value.suiteFiles) {
    require(exact(file, ['path', 'sha256']) && sessionPath(file.path) && sessionHash(file.sha256) && !paths.has(file.path));
    paths.add(file.path);
  }
  require(paths.has('aggregate.json'));
  require(exact(value.runtimeIdentity, ['artifact', 'archive'])
    && value.runtimeIdentity.artifact.name === 'comparison-compiled-runtime-identity'
    && exact(value.runtimeIdentity.archive, ['path', 'bytes', 'sha256'])
    && value.runtimeIdentity.archive.path === 'archives/comparison-compiled-runtime-identity.zip'
    && sessionPositive(value.runtimeIdentity.archive.bytes)
    && value.runtimeIdentity.archive.bytes === value.runtimeIdentity.artifact.sizeInBytes
    && 'sha256:' + value.runtimeIdentity.archive.sha256 === value.runtimeIdentity.artifact.digest);
  if (value.state === 'metadata_verified') require(value.archives === null && value.inputFiles === null);
  else {
    require(exact(value.archives, ['suite', 'provider']) && Array.isArray(value.inputFiles));
    validateFiles(Object.values(value.archives), false);
    validateFiles(value.inputFiles, false);
    for (const file of value.suiteFiles) require(value.inputFiles.some(input => input.path === 'input/aggregate/' + file.path
      && input.sha256 === file.sha256));
  }
  return value;
}
function validateFiles(files, metadata) {
  const seen = new Set();
  require(files.length <= 10000);
  for (const file of files) {
    require(exact(file, ['path', 'bytes', 'sha256']) && sessionPath(file.path) && (metadata ? sessionCount(file.bytes) : sessionPositive(file.bytes))
      && sessionHash(file.sha256) && !seen.has(file.path.toLowerCase()) && (!metadata || file.path.startsWith('metadata/')));
    seen.add(file.path.toLowerCase());
  }
}
export function sessionSuiteFiles(aggregate, aggregateSha256) {
  const files = [{ path: 'aggregate.json', sha256: aggregateSha256 }];
  for (const worker of aggregate.workers) {
    const prefix = `workers/${worker.id}/`;
    files.push({ path: prefix + 'session.json', sha256: worker.sessionSha256 });
    for (const record of [...worker.session.cases, worker.session.runtimeReport, worker.session.outerRuntime, worker.session.qualificationReport,
      ...(worker.session.resourceReport ? [worker.session.resourceReport] : [])])
      files.push({ path: prefix + record.path, sha256: record.sha256 });
    if (worker.stateSha256 !== null) files.push({ path: prefix + 'session-state.json', sha256: worker.stateSha256 });
  }
  return files;
}

function validateArtifactIdentity(value) {
  require(exact(value, ['id', 'name', 'digest', 'sizeInBytes', 'expired']) && sessionPositive(value.id)
    && typeof value.name === 'string' && /^sha256:[a-f0-9]{64}$/.test(value.digest ?? '')
    && sessionPositive(value.sizeInBytes) && value.expired === false);
}
function validateJobProof(value, cohort, steps, failedAllowed) {
  require(exact(value, ['id', 'name', 'url', 'conclusion', 'steps']) && sessionPositive(value.id)
    && value.url === `https://github.com/${SESSION.repository}/actions/runs/${cohort.runId}/job/${value.id}`
    && (value.conclusion === 'success' || failedAllowed && value.conclusion === 'failure')
    && Array.isArray(value.steps) && value.steps.length === steps.length);
  for (const [index, name] of steps.entries()) require(exact(value.steps[index], ['name', 'conclusion'])
    && value.steps[index].name === name && value.steps[index].conclusion === (failedAllowed && index === 0 ? value.conclusion : 'success'));
}
