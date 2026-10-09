import path from 'node:path';
import { mkdtemp, rm } from 'node:fs/promises';
import { isDeepStrictEqual } from 'node:util';
import { fileURLToPath } from 'node:url';
import { GH, requireGitHub, canonicalJobUrl } from './isolated-github-contract.mjs';
import { validateSuccessfulJob, validateArtifact, uniqueNamed, projectJob, projectArtifact } from './isolated-github-validation.mjs';
import { downloadArtifact } from './isolated-github-api.mjs';
import { inspectNativeZip, extractNativeEntry } from './isolated-github-zip.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { hashRegularFile } from './isolated-github-files.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { readPreparedImages } from './image-bundle-read.mjs';
import { runBounded } from './image-process.mjs';
import { exactKeys, AGGREGATE } from './aggregate-contracts.mjs';

export const FUNCTIONAL = Object.freeze({
  job: 'KeyLoad functional RF3 million-record ingestion', artifact: 'keyload-functional-heavy-load',
  steps: Object.freeze(['Test exclusive heavy-load admission', 'Test ingestion while SDK and MCP reads are active', 'Save original functional load results and image receipts']),
  archive: 'archives/keyload-functional-heavy-load.zip',
  className: 'KeyLoad.IntegrationTests.Features.DocumentStorage.HeavyDocumentLoadRf3Tests',
  methodName: 'MillionAcknowledgedDocumentsRemainCorrectDuringSdkAndOfficialMcpReads',
  files: Object.freeze(['functional/original.trx', 'functional/images/image-receipt.json',
    'functional/images/server-manifest.json', 'functional/images/comparisons-manifest.json']),
  maximumBytes: 33554432,
});

export function selectDocumentFunctionalGate(capture, cohort) {
  const job = validateSuccessfulJob(uniqueNamed(capture.jobs, FUNCTIONAL.job), cohort, FUNCTIONAL.job, FUNCTIONAL.steps);
  const artifact = validateArtifact(uniqueNamed(capture.artifacts, FUNCTIONAL.artifact), capture.run, job,
    FUNCTIONAL.artifact, GH.workerZipBytes);
  return { job, artifact };
}

export function validateDocumentFunctionalProof(proof, cohort) {
  requireGitHub(exactKeys(proof, ['job', 'artifact', 'archive', 'files', 'test', 'server'])
    && exactKeys(proof.job, ['id', 'name', 'url', 'conclusion', 'steps'])
    && Number.isSafeInteger(proof.job.id) && proof.job.id > 0 && proof.job.name === FUNCTIONAL.job
    && proof.job.url === canonicalJobUrl(cohort, proof.job.id) && proof.job.conclusion === 'success'
    && Array.isArray(proof.job.steps) && proof.job.steps.length === FUNCTIONAL.steps.length
    && proof.job.steps.every((step, index) => exactKeys(step, ['name', 'conclusion'])
      && step.name === FUNCTIONAL.steps[index] && step.conclusion === 'success')
    && exactKeys(proof.artifact, ['id', 'name', 'digest', 'sizeInBytes', 'expired'])
    && Number.isSafeInteger(proof.artifact.id) && proof.artifact.id > 0 && proof.artifact.name === FUNCTIONAL.artifact
    && proof.artifact.expired === false && AGGREGATE.zipDigest.test(proof.artifact.digest ?? '')
    && Number.isSafeInteger(proof.artifact.sizeInBytes) && proof.artifact.sizeInBytes > 0 && proof.artifact.sizeInBytes <= GH.workerZipBytes
    && exactKeys(proof.archive, ['path', 'bytes', 'sha256']) && proof.archive.path === FUNCTIONAL.archive
    && proof.archive.bytes === proof.artifact.sizeInBytes && 'sha256:' + proof.archive.sha256 === proof.artifact.digest
    && Array.isArray(proof.files) && proof.files.length === FUNCTIONAL.files.length
    && proof.files.every((file, index) => exactKeys(file, ['path', 'entry', 'bytes', 'sha256'])
      && file.path === FUNCTIONAL.files[index] && typeof file.entry === 'string'
      && /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/u.test(file.entry)
      && !file.entry.split('/').some(part => part === '.' || part === '..')
      && Number.isSafeInteger(file.bytes) && file.bytes > 0 && file.bytes <= FUNCTIONAL.maximumBytes
      && AGGREGATE.digest.test(file.sha256 ?? ''))
    && new Set(proof.files.map(file => file.entry)).size === FUNCTIONAL.files.length
    && exactKeys(proof.test, ['className', 'methodName', 'instanceName', 'count'])
    && proof.test.className === FUNCTIONAL.className && proof.test.methodName === FUNCTIONAL.methodName
    && proof.test.instanceName === FUNCTIONAL.methodName && proof.test.count === 1
    && typeof proof.server === 'string' && /@sha256:[a-f0-9]{64}$/u.test(proof.server));
  return proof;
}

export async function parseDocumentFunctionalTrx(trx, workspace) {
  const script = fileURLToPath(new URL('./document-functional-trx.ps1', import.meta.url));
  const result = await runBounded('pwsh', ['-NoProfile', '-File', script, '-Path', trx],
    { cwd: workspace, timeoutMs: GH.unzipTimeoutMs, maximumOutputBytes: GH.metadataBytes });
  requireGitHub(result.success);
  let value;
  try { value = JSON.parse(result.stdout); } catch { requireGitHub(false); }
  requireGitHub(value.suite === 'rf3-heavy-load' && value.testCount === 1 && Array.isArray(value.cases) && value.cases.length === 1);
  const test = value.cases[0];
  requireGitHub(test.className === FUNCTIONAL.className && test.methodName === FUNCTIONAL.methodName
    && test.instanceName === FUNCTIONAL.methodName);
  return { className: test.className, methodName: test.methodName, instanceName: test.instanceName, count: 1 };
}

function originalEntries(entries) {
  const predicates = [entry => /(?:^|\/)rf3-heavy-load\/.+\.trx$/u.test(entry),
    ...['image-receipt.json', 'server-manifest.json', 'comparisons-manifest.json']
      .map(name => entry => entry.endsWith('/keyload-images/' + name) || entry === 'keyload-images/' + name)];
  return predicates.map(predicate => {
    const matches = entries.filter(predicate);
    requireGitHub(matches.length === 1);
    return matches[0];
  });
}

export async function collectDocumentFunctionalGate(output, capture, context, server) {
  const selected = selectDocumentFunctionalGate(capture, context.cohort);
  const archive = path.join(output, FUNCTIONAL.archive);
  await downloadArtifact(selected.artifact, archive, GH.workerZipBytes, context);
  await validateDownloadedArchive(archive, selected.artifact, GH.workerZipBytes);
  const entries = originalEntries(await inspectNativeZip(archive, [], path.join(output, 'github/functional-inventory.txt'), context));
  await createDirectory(path.join(output, 'functional'));
  await createDirectory(path.join(output, 'functional/images'));
  const files = [];
  for (const [index, entry] of entries.entries()) {
    const raw = await extractNativeEntry(archive, entry, path.join(output, FUNCTIONAL.files[index]), FUNCTIONAL.maximumBytes, context);
    files.push({ path: FUNCTIONAL.files[index], entry, bytes: raw.bytes, sha256: raw.sha256 });
  }
  const raw = await hashRegularFile(archive, GH.workerZipBytes);
  const test = await parseDocumentFunctionalTrx(path.join(output, FUNCTIONAL.files[0]), context.native.workspace);
  const proof = { job: projectJob(selected.job, context.cohort, FUNCTIONAL.steps), artifact: projectArtifact(selected.artifact),
    archive: { path: FUNCTIONAL.archive, bytes: raw.bytes, sha256: raw.sha256 }, files, test, server };
  await verifyDocumentFunctionalGate(output, proof, capture, context.cohort, server, context.native.workspace);
  return proof;
}

export async function verifyDocumentFunctionalGate(input, supplied, capture, cohort, server, workspace = input) {
  const proof = validateDocumentFunctionalProof(supplied, cohort);
  const selected = selectDocumentFunctionalGate(capture, cohort);
  requireGitHub(isDeepStrictEqual(proof.job, projectJob(selected.job, cohort, FUNCTIONAL.steps))
    && isDeepStrictEqual(proof.artifact, projectArtifact(selected.artifact)) && proof.server === server);
  const archive = path.join(input, proof.archive.path);
  const raw = await hashRegularFile(archive, GH.workerZipBytes);
  requireGitHub(raw.bytes === proof.archive.bytes && raw.sha256 === proof.archive.sha256);
  const temporary = await mkdtemp(path.join(input, '.functional-original-'));
  try {
    const context = { native: { workspace } };
    const entries = originalEntries(await inspectNativeZip(archive, [], path.join(temporary, 'inventory.txt'), context));
    for (const [index, file] of proof.files.entries()) {
      requireGitHub(file.entry === entries[index]);
      const actual = await hashRegularFile(path.join(input, file.path), FUNCTIONAL.maximumBytes);
      const original = await extractNativeEntry(archive, file.entry, path.join(temporary, String(index)), FUNCTIONAL.maximumBytes, context);
      requireGitHub(actual.bytes === file.bytes && actual.sha256 === file.sha256
        && original.bytes === actual.bytes && original.sha256 === actual.sha256);
    }
    requireGitHub(isDeepStrictEqual(proof.test, await parseDocumentFunctionalTrx(path.join(input, FUNCTIONAL.files[0]), workspace)));
    const images = await readPreparedImages(path.join(input, 'functional/images'), { sourceSha: cohort.sourceRevision,
      runId: String(cohort.runId), runAttempt: String(cohort.attempt), repository: cohort.repository, ref: cohort.ref });
    requireGitHub(images.receipt.images.server.reference === server);
  } finally { await rm(temporary, { recursive: true, force: true }); }
  return proof;
}
