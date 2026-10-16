import { verifyDocumentFunctionalGate } from './document-functional-gate.mjs';
import { verifySessionOriginalArchive } from './session-original-archives.mjs';
import path from 'node:path';
import { link, readdir } from 'node:fs/promises';
import { isDeepStrictEqual } from 'node:util';
import { readBytes, parseBytes, hashBytes } from './aggregate-files.mjs';
import { readPreparedImages } from './image-bundle-read.mjs';
import { imageJsonFiles } from './isolated-github-images.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { readJson, writeJson, hashRegularFile } from './isolated-github-files.mjs';
import { projectJob, projectArtifact } from './isolated-github-validation.mjs';
import { validateWorkflowRun, flattenPages } from './isolated-github-validation.mjs';
import { selectSessionEvidence } from './session-github-selection.mjs';
import { createSessionPlan } from './session-plan.mjs';
import { readSessionRuntimeIdentity, validateSessionRuntimeImport } from './session-runtime-identity.mjs';
import { validateSessionAggregate } from './session-aggregate-contract.mjs';
export { validateSessionAggregate } from './session-aggregate-contract.mjs';
import { SESSION, exactSession as exact, requireSession as require, sessionRows, sessionHash,
  validateSessionCohort } from './session-contract.mjs';
import { validateServerResourceEvidence, requireComparableSessionResources } from './server-resource-evidence.mjs';
import { validateSessionCase, validateSessionEnvelope, validateSessionNativeResults, validateSessionQualification, validateSessionProfileAccounting } from './session-report-validation.mjs';

const compactCase = report => ({ ...report, lanes: report.lanes.map(({ samples, ...lane }) => lane) });
export async function readOriginalSession(directory, row, cohort, metadata, runtimeIdentity = null) {
  const bytes = await readBytes(path.join(directory, 'session.json'), SESSION.sessionBytes);
  require(hashBytes(bytes) === metadata.sessionSha256);
  const session = validateSessionEnvelope(parseBytes(bytes), row, cohort, metadata.job.id);
  const runtimeBytes = await readBytes(path.join(directory, session.outerRuntime.path), SESSION.sessionBytes);
  require(hashBytes(runtimeBytes) === session.outerRuntime.sha256);
  validateSessionRuntimeImport(parseBytes(runtimeBytes), cohort, runtimeIdentity);
  const reports = [];
  for (const record of session.cases) {
    const raw = await readBytes(path.join(directory, record.path), SESSION.reportBytes);
    require(hashBytes(raw) === record.sha256);
    reports.push(validateSessionCase(parseBytes(raw), record.id));
  }
  const nativeBytes = await readBytes(path.join(directory, session.nativeReport.path), SESSION.nativeBytes);
  require(hashBytes(nativeBytes) === session.nativeReport.sha256);
  const native = validateSessionNativeResults(nativeBytes.toString('utf8'), reports);
  const qualificationBytes = await readBytes(path.join(directory, session.qualificationReport.path), SESSION.sessionBytes);
  require(hashBytes(qualificationBytes) === session.qualificationReport.sha256);
  const qualification = validateSessionQualification(parseBytes(qualificationBytes),session,native);
  validateSessionProfileAccounting(session,reports,native,qualification);
  const failed = qualification.nativeExitCode !== 0 || qualification.status === 'failed' || reports.some(report => ['failed', 'notQualified'].includes(report.status)) || native.some(item => item.outcome === 'Failed');
  require(session.outcome === (failed ? 'failure' : session.targetProfile === null ? 'unavailable' : 'success')
    && (failed ? metadata.job.conclusion === 'failure' : metadata.job.conclusion === 'success'));
  let resources = null;
  let stateSha256 = null;
  let resourceProof = null;
  if (session.resourceReport !== null) {
    const raw = await readBytes(path.join(directory, session.resourceReport.path), SESSION.nativeBytes);
    require(hashBytes(raw) === session.resourceReport.sha256);
    resources = parseBytes(raw);
    require(exact(resources, ['schemaVersion', 'sessionId', 'sourceRevision', 'runId', 'attempt', 'jobId', 'target', 'nodeCount', 'sessionStateSha256', 'observations'])
      && resources.schemaVersion === 'session-resource-evidence.v1' && resources.sessionId === session.sessionId
      && resources.sourceRevision === cohort.sourceRevision && resources.runId === String(cohort.runId)
      && resources.attempt === String(cohort.attempt) && resources.jobId === String(session.jobId)
      && resources.target === row.target && resources.nodeCount === row.nodeCount
      && typeof resources.observations?.qualified === 'boolean');
    require(exact(resources.observations, ['hardware', 'appHostEnvelope', 'containers', 'missingEvidence', 'qualified', 'observationPolicy']));
    // Reuse the native observation contract; this maps field names, never manufactures a sample.
    resourceProof = validateServerResourceEvidence({ schema: 'server-resource-evidence.v2',
      sourceRevision: resources.sourceRevision, workflowRunId: resources.runId, runAttempt: resources.attempt,
      jobId: resources.jobId, target: resources.target, nodeCount: resources.nodeCount, scenario: 'session',
      profile: SESSION.profile, workerSha256: resources.sessionStateSha256, ...resources.observations },
      session.resourceReport.sha256, resources.sessionStateSha256, { ...row, scenario: 'session', profile: SESSION.profile },
      cohort, session.jobId, true);
    const stateBytes = await readBytes(path.join(directory, 'session-state.json'), SESSION.sessionBytes);
    stateSha256 = hashBytes(stateBytes);
    require(stateSha256 === resources.sessionStateSha256);
    const state = parseBytes(stateBytes);
    const { nativeReport, resourceReport, outerRuntime, qualificationReport, ...body } = session;
    require(isDeepStrictEqual({ ...state, outcome: body.outcome, reason: body.reason }, body));
  }
  return { session, reports, native, resources, stateSha256, resourceProof, qualification };
}
async function authenticateProof(input, proof) {
  require(exact(proof, ['schemaVersion', 'cohort', 'workers', 'preparation', 'images']) && proof.schemaVersion === SESSION.schema);
  const directory = path.join(input, 'github');
  const capture = { run: await readJson(path.join(directory, 'run-attempt.json')),
    jobs: flattenPages(await readJson(path.join(directory, 'jobs-pages.json')), 'jobs'),
    artifacts: flattenPages(await readJson(path.join(directory, 'artifacts-pages.json')), 'artifacts') };
  validateWorkflowRun(await readJson(path.join(directory, 'workflow.json')), capture.run, proof.cohort);
  const selected = selectSessionEvidence(capture, proof.cohort);
  const expected = selected.workers.map(({ row, job, artifact }) => ({ id: row.id, target: row.target, nodeCount: row.nodeCount,
    job: projectJob(job, proof.cohort, SESSION.workerSteps), artifact: projectArtifact(artifact) }));
  require(Array.isArray(proof.workers) && proof.workers.length === expected.length);
  for (const [index, worker] of proof.workers.entries()) {
    const { sessionSha256, ...metadata } = worker;
    require(sessionHash(sessionSha256) && isDeepStrictEqual(metadata, expected[index]));
  }
  const image = await readJson(path.join(directory, 'image-proof.json'));
  require(isDeepStrictEqual(image.job, projectJob(selected.image.job, proof.cohort, ['Check Docker image export and import', 'Save Docker images']))
    && isDeepStrictEqual(image.artifact, projectArtifact(selected.image.artifact))
    && isDeepStrictEqual(proof.images, image.images) && isDeepStrictEqual(proof.preparation,
    { job: image.job, artifact: image.artifact, compiledArtifact: projectArtifact(selected.compiledArtifact), runtimeIdentity: proof.preparation.runtimeIdentity, functional: proof.preparation.functional }));
  const runtime = proof.preparation.runtimeIdentity;
  require(isDeepStrictEqual(runtime.artifact, projectArtifact(selected.runtimeArtifact)));
  const runtimeArchive = await hashRegularFile(path.join(input, runtime.archive.path), selected.runtimeArtifact.size_in_bytes);
  require(runtimeArchive.sha256 === runtime.archive.sha256 && 'sha256:' + runtimeArchive.sha256 === selected.runtimeArtifact.digest);
  const identity = await readSessionRuntimeIdentity(path.join(directory, 'runtime'), proof.cohort);
  require(identity.descriptorSha256 === runtime.descriptorSha256 && identity.inventorySha256 === runtime.inventorySha256);
  require(Array.isArray(runtime.files) && runtime.files.length === 2
    && runtime.files.every((file,index) => exact(file,['path','bytes','sha256'])
      && file.path === ['github/runtime/benchmark-runtime.json','github/runtime/benchmark-runtime-files.json'][index]
      && file.sha256 === [identity.descriptorSha256,identity.inventorySha256][index]));
  require(Array.isArray(image.files) && image.files.length === imageJsonFiles.length
    && image.files.every((file,index) => exact(file,['path','bytes','sha256'])
      && file.path === 'github/images/' + imageJsonFiles[index] && sessionHash(file.sha256)));
  const prepared = await readPreparedImages(path.join(directory,'images'),{sourceSha:proof.cohort.sourceRevision,
    runId:String(proof.cohort.runId),runAttempt:String(proof.cohort.attempt),repository:proof.cohort.repository,ref:proof.cohort.ref});
  require(prepared.receipt.images.server.reference === proof.images.server
    && prepared.receipt.images.comparisons.reference === proof.images.loadGenerator);
  await verifyDocumentFunctionalGate(input, proof.preparation.functional, capture, proof.cohort, proof.images.server);
  await verifySessionOriginalArchive(input, path.join(input, runtime.archive.path), runtime.artifact,
    runtime.files.map(file => ({...file,entry:path.basename(file.path),maximum:67_108_864})), 134_217_728);
  await verifySessionOriginalArchive(input, path.join(input, image.archive.path), image.artifact,
    image.files.map(file => ({...file,entry:path.basename(file.path),maximum:SESSION.nativeBytes})), 9_663_676_416);
  return identity;
}
export async function aggregateSessions({ input, output }) {
  const proof = await readJson(path.join(input, 'proof.json'));
  const runtimeIdentity = await authenticateProof(input, proof);
  const rows = sessionRows();
  const inventory = await readdir(path.join(input, 'workers'), { withFileTypes: true });
  require(inventory.length === rows.length && inventory.every(entry => entry.isDirectory() && !entry.isSymbolicLink()
    && rows.some(row => row.id === entry.name)));
  const workers = [];
  const originals = [];
  const comparableResources = [];
  for (const [index, row] of rows.entries()) {
    const metadata = proof.workers[index];
    const original = await readOriginalSession(path.join(input, 'workers', row.id), row, proof.cohort, metadata, runtimeIdentity);
    const prefix = `workers/${row.id}/`;
    const references = [{entry:'session.json',path:prefix+'session.json',sha256:metadata.sessionSha256},
      ...[...original.session.cases,original.session.nativeReport,original.session.outerRuntime,original.session.qualificationReport,
        ...(original.session.resourceReport ? [original.session.resourceReport,{path:'session-state.json',sha256:original.stateSha256}] : [])]
        .map(file => ({...file,entry:file.path,path:prefix+file.path}))];
    await verifySessionOriginalArchive(input,path.join(input,'archives',row.id+'.zip'),metadata.artifact,references,SESSION.archiveBytes);
    if (original.resourceProof !== null) comparableResources.push({nodeCount:row.nodeCount,proof:original.resourceProof});
    workers.push({ ...metadata, session: original.session, reports: original.reports.map(compactCase), native: original.native, qualification: original.qualification, resourceQualified: original.resources?.observations.qualified ?? false, stateSha256: original.stateSha256 });
    originals.push(original.session);
  }
  requireComparableSessionResources(comparableResources);
  const aggregate = validateSessionAggregate({ schemaVersion: SESSION.schema, cohort: proof.cohort, plan: createSessionPlan(),
    images: proof.images, preparation: proof.preparation, workers });
  await createDirectory(output);
  await createDirectory(path.join(output, 'workers'));
  for (const [index, row] of rows.entries()) {
    const target = await createDirectory(path.join(output, 'workers', row.id));
    const paths = ['session.json', ...originals[index].cases.map(record => record.path), originals[index].nativeReport.path, originals[index].outerRuntime.path, originals[index].qualificationReport.path,
      ...(originals[index].resourceReport ? [originals[index].resourceReport.path, 'session-state.json'] : [])];
    const directories = new Set([target]);
    for (const relative of paths) {
      let parent = target;
      for (const component of relative.split('/').slice(0, -1)) {
        parent = path.join(parent, component);
        if (!directories.has(parent)) { await createDirectory(parent); directories.add(parent); }
      }
      const original = path.join(input, 'workers', row.id, relative);
      await hashRegularFile(original, relative.endsWith('.trx') ? SESSION.nativeBytes : SESSION.reportBytes);
      await link(original, path.join(target, relative));
    }
  }
  await writeJson(path.join(output, 'aggregate.json'), aggregate);
  return aggregate;
}
