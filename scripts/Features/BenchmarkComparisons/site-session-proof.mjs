import { verifyDocumentFunctionalGate } from './document-functional-gate.mjs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { readJson, hashRegularFile } from './isolated-github-files.mjs';
import { flattenPages, projectJob, projectArtifact } from './isolated-github-validation.mjs';
import { SESSION, requireSession, sessionPath, sessionHash, sessionCount, exactSession } from './session-contract.mjs';
import { validateSessionAggregate } from './session-aggregate-contract.mjs';
import { selectSessionSiteProducer, requirePublishableSessionMeasurements } from './site-session-selection.mjs';
import { validateSessionSiteReceipt, sessionSuiteFiles } from './site-session-receipt.mjs';
import { readSessionRuntimeIdentity, validateSessionRuntimeImport } from './session-runtime-identity.mjs';
import { validateSessionProcesses } from './session-process-identity.mjs';
import { produceIsolatedProjection } from '../../../site/Features/BenchmarkComparisons/isolated-projection.mjs';

async function verifyFiles(input, files) {
  requireSession(Array.isArray(files) && files.length > 0 && files.length <= 10000);
  const seen = new Set();
  for (const file of files) {
    requireSession(exactSession(file, ['path','bytes','sha256']) && sessionPath(file.path)
      && sessionCount(file.bytes) && sessionHash(file.sha256) && !seen.has(file.path.toLowerCase()));
    seen.add(file.path.toLowerCase());
    const actual = await hashRegularFile(path.join(input, file.path), file.bytes, file.bytes === 0);
    requireSession(actual.bytes === file.bytes && actual.sha256 === file.sha256);
  }
}
export async function verifySessionSiteInputs({ input, receipt: receiptPath }) {
  const receipt = validateSessionSiteReceipt(await readJson(receiptPath, SESSION.aggregateBytes));
  requireSession(receipt.state === 'archive_verified');
  await verifyFiles(input, receipt.metadataFiles);
  await verifyFiles(input, Object.values(receipt.archives));
  await verifyFiles(input, receipt.workerArchives);
  await verifyFiles(input, [receipt.runtimeIdentity.archive, receipt.functional.archive]);
  await verifyFiles(input, receipt.inputFiles);
  const metadata = validateSessionSiteReceipt(await readJson(path.join(input, 'metadata-proof.json')));
  requireSession(isDeepStrictEqual({ ...receipt, state: 'metadata_verified', archives: null, inputFiles: null }, metadata));
  const directory = path.join(input, 'metadata', String(receipt.run.id));
  const workflow = await readJson(path.join(input, 'metadata', 'workflow.json'));
  const capture = { run: await readJson(path.join(directory, 'run-attempt.json')),
    jobs: flattenPages(await readJson(path.join(directory, 'jobs-pages.json')), 'jobs'),
    artifacts: flattenPages(await readJson(path.join(directory, 'artifacts-pages.json')), 'artifacts') };
  const selected = selectSessionSiteProducer(capture, workflow, receipt.mode);
  requireSession(selected !== null && selected.job.id === receipt.aggregateJob.id
    && selected.suite.id === receipt.artifacts.suite.id && selected.provider.id === receipt.artifacts.provider.id
    && selected.suite.digest === receipt.artifacts.suite.digest && selected.provider.digest === receipt.artifacts.provider.digest);
  const aggregateFile = path.join(input, 'input', 'aggregate', 'aggregate.json');
  const aggregate = validateSessionAggregate(await readJson(aggregateFile, SESSION.aggregateBytes));
  if (receipt.mode === 'publish') requirePublishableSessionMeasurements(aggregate);
  const aggregateHash = await hashRegularFile(aggregateFile, SESSION.aggregateBytes);
  requireSession(isDeepStrictEqual(sessionSuiteFiles(aggregate, aggregateHash.sha256), receipt.suiteFiles));
  for (const [index, original] of selected.execution.workers.entries()) {
    const worker = aggregate.workers[index];
    requireSession(worker.id === original.row.id && worker.job.id === original.job.id && worker.artifact.id === original.artifact.id
      && worker.artifact.digest === original.artifact.digest);
  }
  const providerRoot = path.join(input, 'input', 'provider');
  requireSession(isDeepStrictEqual(receipt.preparation.job, projectJob(selected.execution.preparation, receipt.cohort, SESSION.prepareSteps))
    && isDeepStrictEqual(receipt.preparation.compiledArtifact, projectArtifact(selected.execution.compiledArtifact))
    && isDeepStrictEqual(receipt.preparation.job, aggregate.preparation.job)
    && isDeepStrictEqual(receipt.preparation.compiledArtifact, aggregate.preparation.compiledArtifact));
  const runtimeIdentity = await readSessionRuntimeIdentity(path.join(providerRoot, 'github', 'runtime'), receipt.cohort);
  requireSession(isDeepStrictEqual(receipt.runtimeIdentity.artifact, aggregate.preparation.runtimeIdentity.artifact)
    && receipt.runtimeIdentity.artifact.id === selected.execution.runtimeArtifact.id
    && runtimeIdentity.descriptorSha256 === aggregate.preparation.runtimeIdentity.descriptorSha256
    && runtimeIdentity.inventorySha256 === aggregate.preparation.runtimeIdentity.inventorySha256);
  for (const worker of aggregate.workers) {
    const file = path.join(input, 'input', 'aggregate', 'workers', worker.id, worker.session.outerRuntime.path);
    validateSessionRuntimeImport(await readJson(file, SESSION.sessionBytes), receipt.cohort, runtimeIdentity);
    validateSessionProcesses(worker.session.processes, runtimeIdentity);
  }
  const functional = aggregate.preparation.functional;
  requireSession(isDeepStrictEqual(receipt.functional.artifact, functional.artifact));
  // Verify against the original archive in the capture root; provider files remain extracted originals.
  await verifyDocumentFunctionalGate(input, functional, capture, receipt.cohort, runtimeIdentity, input, providerRoot);
  await produceIsolatedProjection({ input: path.join(input, 'input', 'aggregate') });
  const proof = await readJson(path.join(input, 'input', 'provider', 'proof.json'));
  requireSession(isDeepStrictEqual(proof.cohort, aggregate.cohort)
    && isDeepStrictEqual(proof.preparation, aggregate.preparation)
    && isDeepStrictEqual(proof.workers, aggregate.workers.map(({session,reports,execution,qualification,resourceQualified,stateSha256,...worker}) => worker)));
  return { workers: aggregate.workers.length, files: receipt.inputFiles.length };
}
function identity(receipt) {
  return { source: receipt.source, cohort: receipt.cohort, aggregateJobId: receipt.aggregateJob?.id ?? receipt.aggregateJobId,
    suiteArtifactId: receipt.artifacts?.suite.id ?? receipt.suiteArtifactId,
    providerArtifactId: receipt.artifacts?.provider.id ?? receipt.providerArtifactId,
    preparation: receipt.preparation?.job ? {jobId:receipt.preparation.job.id,runtimeArtifactId:receipt.runtimeIdentity.artifact.id,
      runtimeDigest:receipt.runtimeIdentity.artifact.digest,compiledArtifactId:receipt.preparation.compiledArtifact.id,
      compiledDigest:receipt.preparation.compiledArtifact.digest,functionalArtifactId:receipt.functional.artifact.id,
      functionalDigest:receipt.functional.artifact.digest} : receipt.preparation,
    workers: receipt.workers.map(worker => ({ id: worker.id, jobId: worker.job?.id ?? worker.jobId,
      artifactId: worker.artifact?.id ?? worker.artifactId, digest: worker.artifact?.digest ?? worker.digest })) };
}
export async function verifySessionSiteFreshness({ before, after }) {
  const previous = await readJson(before), current = await readJson(after);
  if (previous.state === 'unavailable' || current.state === 'unavailable') {
    validateSessionSiteReceipt(previous); validateSessionSiteReceipt(current);
    requireSession(previous.state === current.state && isDeepStrictEqual(previous.source, current.source));
    await verifyFiles(path.dirname(before), previous.metadataFiles);
    await verifyFiles(path.dirname(after), current.metadataFiles);
    return { fresh: true, state: 'unavailable', source: current.source, producer: null };
  }
  validateSessionSiteReceipt(previous);
  requireSession(current.schemaVersion === SESSION.schema && current.state === 'selection_verified'
    && previous.state === 'archive_verified' && previous.publishEligible
    && exactSession(current, ['schemaVersion','state','source','cohort','aggregateJobId','suiteArtifactId',
      'providerArtifactId','preparation','workers','metadataFiles'])
    && Array.isArray(current.metadataFiles) && current.metadataFiles.every(file => file.path?.startsWith('metadata/'))
    && isDeepStrictEqual(identity(previous), identity(current)));
  await verifyFiles(path.dirname(after), current.metadataFiles);
  return { fresh: true, ...identity(current) };
}
