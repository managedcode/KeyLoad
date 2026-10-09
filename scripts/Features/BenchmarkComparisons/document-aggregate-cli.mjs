import path from 'node:path';
import { mkdtemp, rm } from 'node:fs/promises';
import { streamToFile } from './isolated-github-stream.mjs';
import { selectDocumentWorkers } from './document-github-collect.mjs';
import { readPreparedImages } from './image-bundle-read.mjs';
import { imageJsonFiles } from './isolated-github-images.mjs';
import { flattenPages, validateWorkflowRun, projectJob, projectArtifact, validateSuccessfulJob, validateArtifact, uniqueNamed } from './isolated-github-validation.mjs';
import { pathToFileURL } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, KEYS, exactKeys, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { DOCUMENT, documentJobName, validateDocumentPlan } from './document-isolated-plan.mjs';
import { documentStatistics, validateDocumentComparableReports, validateDocumentEnvelope } from './document-evidence.mjs';
import { absolutePath } from './aggregate-files.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { readJson, hashRegularFile, writeJson } from './isolated-github-files.mjs';
import { canonicalJobUrl, GH } from './isolated-github-contract.mjs';
import { validateServerResourceEvidence, requireComparableScaleProfiles } from './server-resource-evidence.mjs';
import { validateDocumentFunctionalProof, verifyDocumentFunctionalGate } from './document-functional-gate.mjs';

const check = condition => requireValue(condition, 'E_DOCUMENT_AGGREGATE');
export function validateDocumentIntake(intake, plan) {
  check(exactKeys(intake, ['schemaVersion', 'family', 'cohort', 'functionalGate', 'cells']) && intake.schemaVersion === 1
    && intake.family === DOCUMENT.family && Array.isArray(intake.cells) && intake.cells.length === plan.cells.length);
  validateCohort(intake.cohort, intake.cohort.profile);
  validateDocumentFunctionalProof(intake.functionalGate, intake.cohort);
  const jobs = new Set(); const artifacts = new Set();
  for (const [index, item] of intake.cells.entries()) {
    const cell = plan.cells[index]; const proof = item?.proof;
    check(exactKeys(item, ['cell', 'proof', 'rawPath', 'resourcePath']) && isDeepStrictEqual(item.cell, cell)
      && item.rawPath === `workers/${cell.id}/${DOCUMENT.raw}` && item.resourcePath === `workers/${cell.id}/server-resource-evidence.json`
      && exactKeys(proof, KEYS.scaleProofCell) && proof.id === cell.id && AGGREGATE.digest.test(proof.workerSha256 ?? '')
      && exactKeys(proof.job, KEYS.job) && Number.isSafeInteger(proof.job.id) && proof.job.id > 0
      && proof.job.name === documentJobName(cell) && proof.job.url === canonicalJobUrl(intake.cohort, proof.job.id)
      && ['success', 'failure'].includes(proof.job.conclusion) && Array.isArray(proof.job.steps) && proof.job.steps.length === 2
      && proof.job.steps.every((step, index) => exactKeys(step, KEYS.step) && step.name === AGGREGATE.steps[index]
        && step.conclusion === (index === 0 ? proof.job.conclusion : 'success'))
      && exactKeys(proof.artifact, KEYS.artifact) && Number.isSafeInteger(proof.artifact.id) && proof.artifact.id > 0
      && proof.artifact.name === DOCUMENT.artifactPrefix + cell.id && proof.artifact.expired === false
      && Number.isSafeInteger(proof.artifact.sizeInBytes) && proof.artifact.sizeInBytes > 0 && proof.artifact.sizeInBytes <= GH.workerZipBytes
      && AGGREGATE.zipDigest.test(proof.artifact.digest ?? '') && !jobs.has(proof.job.id) && !artifacts.has(proof.artifact.id));
    jobs.add(proof.job.id); artifacts.add(proof.artifact.id);
  }
  return intake;
}
export async function aggregateDocumentEvidence(input, output) {
  const plan = validateDocumentPlan(await readJson(path.join(input, 'document-plan.json'), GH.metadataBytes));
  const intake = validateDocumentIntake(await readJson(path.join(input, 'document-intake.json'), GH.metadataBytes), plan);
  const workflow = await readJson(path.join(input, 'github/workflow.json'), GH.metadataBytes);
  const run = await readJson(path.join(input, 'github/run-attempt.json'), GH.metadataBytes);
  validateWorkflowRun(workflow, run, intake.cohort);
  const capture = { run, jobs: flattenPages(await readJson(path.join(input, 'github/jobs-pages.json'), GH.metadataBytes), 'jobs'),
    artifacts: flattenPages(await readJson(path.join(input, 'github/artifacts-pages.json'), GH.metadataBytes), 'artifacts') };
  const selected = selectDocumentWorkers(capture, { cohort: intake.cohort }, plan);
  const image = await validateDocumentImages(input, capture, intake.cohort);
  const functionalGate = await verifyDocumentFunctionalGate(input, intake.functionalGate, capture, intake.cohort, image.server);
  const cells = [];
  let totalBytes = 0;
  for (const [index, item] of intake.cells.entries()) {
    check(isDeepStrictEqual(item.proof.job, projectJob(selected[index].job, intake.cohort, GH.workerSteps))
      && isDeepStrictEqual(item.proof.artifact, projectArtifact(selected[index].artifact)));
    const archive = path.join(input, 'archives', item.cell.id + '.zip');
    const zip = await hashRegularFile(archive, GH.workerZipBytes);
    check(zip.bytes === item.proof.artifact.sizeInBytes && 'sha256:' + zip.sha256 === item.proof.artifact.digest);
    const cohort = { ...intake.cohort, profile: item.cell.profile };
    const rawPath = path.join(input, item.rawPath);
    const raw = await hashRegularFile(rawPath, GH.workerRawBytes);
    totalBytes += raw.bytes;
    check(raw.sha256 === item.proof.workerSha256 && totalBytes <= AGGREGATE.totalBytes);
    await verifyArchivedEntry(input, archive, DOCUMENT.raw, raw.sha256, GH.workerRawBytes);
    const value = validateDocumentEnvelope(await readJson(rawPath, GH.workerRawBytes), item.cell, cohort);
    check(value.worker.jobId === item.proof.job.id && ((value.disposition === 'failed') === (item.proof.job.conclusion === 'failure')));
    if (value.report !== null && item.cell.target === 'KeyLoad') check(value.report.repetitions.every(rep => rep.target.image === image.server));
    const sidecarPath = path.join(input, item.resourcePath);
    const resourceRaw = await hashRegularFile(sidecarPath, GH.serverResourceBytes);
    await verifyArchivedEntry(input, archive, 'server-resource-evidence.json', resourceRaw.sha256, GH.serverResourceBytes);
    const resource = validateServerResourceEvidence(await readJson(sidecarPath, GH.serverResourceBytes), resourceRaw.sha256,
      raw.sha256, item.cell, cohort, value.worker.jobId, value.disposition !== 'unsupportedTopology');
    check(isDeepStrictEqual(resource, item.proof.serverResource));
    cells.push({ ...item, disposition: value.disposition, reason: value.reason, report: value.report,
      statistics: value.disposition === 'measured' ? documentStatistics(value.report, item.cell) : null });
  }
  validateDocumentComparableReports(cells);
  const profiles = [...new Set(cells.map(item => item.cell.profile))].map(profile => ({ profile,
    workers: cells.filter(item => item.cell.profile === profile).map(item => ({ nodeCount: item.cell.nodeCount,
      scenario: item.cell.documentScenario, disposition: item.disposition,
      serverResourceQualified: item.proof.serverResource.qualified, resourceEquivalence: item.proof.serverResource.comparison })) }));
  requireComparableScaleProfiles(profiles);
  await createDirectory(output);
  const qualified = cells.every(item => item.disposition !== 'failed' && (item.disposition !== 'measured' || item.proof.serverResource.qualified));
  const aggregate = { schemaVersion: 1, family: DOCUMENT.family, cohort: intake.cohort, functionalGate, qualified, cells };
  await writeJson(path.join(output, 'document-aggregate.json'), aggregate);
  return aggregate;
}
async function verifyArchivedEntry(input, archive, entry, sha256, limit) {
  const temporary = await mkdtemp(path.join(input, '.document-original-'));
  try {
    const raw = await streamToFile('unzip', ['-p', archive, entry], path.join(temporary, 'entry'), limit, GH.unzipTimeoutMs, input);
    check(raw.sha256 === sha256);
  } finally { await rm(temporary, { recursive: true, force: true }); }
}
async function validateDocumentImages(input, capture, cohort) {
  const image = await readJson(path.join(input, 'github/image-proof.json'), GH.metadataBytes);
  const job = validateSuccessfulJob(uniqueNamed(capture.jobs, GH.imageJob), cohort, GH.imageJob, GH.imageSteps);
  const artifact = validateArtifact(uniqueNamed(capture.artifacts, GH.imageArtifact), capture.run, job, GH.imageArtifact, GH.imageZipBytes);
  check(exactKeys(image, ['schemaVersion', 'cohort', 'job', 'artifact', 'archive', 'files', 'images']) && image.schemaVersion === 1
    && isDeepStrictEqual(image.cohort, cohort) && isDeepStrictEqual(image.job, projectJob(job, cohort, GH.imageSteps))
    && isDeepStrictEqual(image.artifact, projectArtifact(artifact)) && exactKeys(image.archive, ['path', 'bytes', 'sha256'])
    && image.archive.path === 'archives/comparison-image-bundle.zip' && Array.isArray(image.files) && image.files.length === 4);
  const archive = path.join(input, image.archive.path);
  const actual = await hashRegularFile(archive, GH.imageZipBytes);
  check(actual.bytes === artifact.size_in_bytes && 'sha256:' + actual.sha256 === artifact.digest
    && image.archive.bytes === actual.bytes && image.archive.sha256 === actual.sha256);
  check(new Set(image.files.map(file => file.path)).size === imageJsonFiles.length);
  for (const file of image.files) {
    check(exactKeys(file, ['path', 'bytes', 'sha256']) && imageJsonFiles.some(name => file.path === 'github/images/' + name));
    const raw = await hashRegularFile(path.join(input, file.path), GH.metadataBytes);
    check(raw.bytes === file.bytes && raw.sha256 === file.sha256);
    await verifyArchivedEntry(input, archive, path.basename(file.path), raw.sha256, GH.metadataBytes);
  }
  const native = { sourceSha: cohort.sourceRevision, runId: String(cohort.runId), runAttempt: String(cohort.attempt), repository: cohort.repository, ref: cohort.ref };
  const prepared = await readPreparedImages(path.join(input, 'github/images'), native);
  check(exactKeys(image.images, ['server', 'loadGenerator']) && image.images.server === prepared.receipt.images.server.reference
    && image.images.loadGenerator === prepared.receipt.images.comparisons.reference);
  return image.images;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const entries = process.argv.slice(2).map(argument => /^--(input|output)=(.+)$/u.exec(argument));
    check(entries.length === 2 && entries.every(Boolean) && new Set(entries.map(item => item[1])).size === 2);
    const values = Object.fromEntries(entries.map(item => [item[1], absolutePath(item[2])]));
    await aggregateDocumentEvidence(values.input, values.output);
  } catch { process.stderr.write('Document benchmark evidence rejected.\n'); process.exitCode = 1; }
}
