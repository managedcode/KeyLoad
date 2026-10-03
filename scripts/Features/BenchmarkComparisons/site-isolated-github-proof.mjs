import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { readJson } from './isolated-github-files.mjs';
import { GH, hashPattern, positive } from './isolated-github-contract.mjs';
import { flattenPages, projectJob, uniqueNamed, validateArtifact } from './isolated-github-validation.mjs';
import { selectCompletedEvidence } from './isolated-github-selection.mjs';
import { createIsolatedPlan } from './isolated-plan.mjs';
import { validateAggregateProof } from './aggregate-proof.mjs';
import { readPreparedImages } from './image-bundle-read.mjs';
import { requireWorkerImages } from './isolated-github-images.mjs';
import { produceIsolatedProjection } from '../../../site/Features/BenchmarkComparisons/isolated-projection.mjs';
import { SITE_GH, exact, plainArtifact, projectSiteArtifact, requireSite } from './site-isolated-github-contract.mjs';
import { validateSiteIsolatedReceipt } from './site-isolated-github-receipt.mjs';
import { selectSiteIsolatedEvidence, siteCohort } from './site-isolated-github-runs.mjs';
import { siteMetadataFiles, verifySiteFiles } from './site-isolated-github-files.mjs';
import { verifyOriginalSiteEntries } from './site-isolated-github-native-proof.mjs';

export async function proveSiteIsolatedEvidence({ input, selection, source, mode }) {
  requireSite(selection?.state === 'selected');
  const { run, job, jobs, workflow } = selection;
  const cohort = siteCohort(run);
  const artifacts = flattenPages(await readJson(path.join(input, SITE_GH.metadata, 'artifacts-pages.json')), 'artifacts');
  const selected = selectCompletedEvidence({ run, jobs, artifacts }, { cohort }, createIsolatedPlan());
  const projectedArtifacts = {};
  for (const name of ['suite', 'provider']) {
    const bound = name === 'suite' ? SITE_GH.suiteBytes : SITE_GH.providerBytes;
    projectedArtifacts[name] = projectSiteArtifact(validateArtifact(uniqueNamed(artifacts, SITE_GH[name]), run, job, SITE_GH[name], bound));
  }
  return validateSiteIsolatedReceipt({ schemaVersion: 1, state: SITE_GH.metadataState, mode,
    publishEligible: mode === SITE_GH.publish, source: { ...source, measured: run.head_sha },
    repository: { id: run.repository.id, fullName: run.repository.full_name }, workflow: { id: workflow.id, path: workflow.path },
    run: { id: run.id, number: run.run_number, attempt: run.run_attempt, url: run.html_url }, cohort,
    aggregateJob: { id: job.id, name: job.name, url: `https://github.com/${SITE_GH.repository}/actions/runs/${run.id}/job/${job.id}`,
      startedAt: job.started_at, completedAt: job.completed_at,
      steps: SITE_GH.steps.map(name => { const step = job.steps.find(item => item.name === name); return { name, number: step.number, conclusion: step.conclusion }; }) },
    artifacts: projectedArtifacts,
    workers: selected.cells.map(item => ({ id: item.cell.id, job: projectJob(item.job, cohort, GH.workerSteps), artifact: projectSiteArtifact(item.artifact) })),
    image: { job: projectJob(selected.image.job, cohort, GH.imageSteps), artifact: projectSiteArtifact(selected.image.artifact) },
    metadataFiles: await siteMetadataFiles(input), archives: null, inputFiles: null });
}

async function verifyImages(input, receipt) {
  const provider = path.join(input, 'input', 'provider');
  const image = await readJson(path.join(provider, 'image-proof.json'), SITE_GH.jsonBytes);
  requireSite(exact(image, ['schemaVersion', 'cohort', 'job', 'artifact', 'archive', 'files', 'images']) && image.schemaVersion === 1
    && isDeepStrictEqual(image.cohort, receipt.cohort) && isDeepStrictEqual(image.job, receipt.image.job)
    && isDeepStrictEqual(image.artifact, plainArtifact(receipt.image.artifact)) && exact(image.archive, SITE_GH.fileKeys)
    && image.archive.path === 'archives/comparison-image-bundle.zip' && image.archive.bytes === image.artifact.sizeInBytes
    && `sha256:${image.archive.sha256}` === image.artifact.digest && Array.isArray(image.files) && image.files.length === 4);
  const files = image.files.map(file => {
    requireSite(exact(file, SITE_GH.fileKeys) && positive(file.bytes) && file.bytes <= SITE_GH.jsonBytes && hashPattern.test(file.sha256 ?? '')
      && SITE_GH.providerFiles.slice(2).includes(file.path.replace(/^github\//, '')));
    return { ...file, path: 'input/provider/' + file.path.slice('github/'.length) };
  });
  requireSite(new Set(files.map(file => file.path)).size === 4);
  await verifySiteFiles(input, files);
  const native = { sourceSha: receipt.cohort.sourceRevision, runId: String(receipt.run.id), runAttempt: String(receipt.run.attempt), repository: SITE_GH.repository, ref: receipt.cohort.ref };
  const prepared = await readPreparedImages(path.join(provider, 'images'), native);
  requireSite(exact(image.images, ['server', 'loadGenerator']) && image.images.server === prepared.receipt.images.server.reference
    && image.images.loadGenerator === prepared.receipt.images.comparisons.reference);
  const bundle = await readJson(path.join(provider, 'images', 'image-bundle.json'), SITE_GH.jsonBytes);
  requireSite(exact(bundle, ['schemaVersion', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'images']) && bundle.schemaVersion === 1
    && bundle.sourceRevision === native.sourceSha && bundle.runId === native.runId && bundle.attempt === native.runAttempt
    && bundle.repository === native.repository && bundle.ref === native.ref && exact(bundle.images, ['server', 'comparisons']));
  for (const name of ['server', 'comparisons']) {
    const record = bundle.images[name];
    requireSite(exact(record, ['archive', 'bytes', 'sha256']) && record.archive === name + '.tar' && positive(record.bytes)
      && record.bytes <= 4_294_967_296 && hashPattern.test(record.sha256 ?? ''));
  }
  return image.images;
}

export async function validateSiteIsolatedInputs({ input, receipt: receiptPath }) {
  const receipt = validateSiteIsolatedReceipt(await readJson(receiptPath, SITE_GH.jsonBytes));
  requireSite(receipt.state === SITE_GH.archiveState);
  await verifySiteFiles(input, receipt.metadataFiles);
  await verifySiteFiles(input, Object.values(receipt.archives));
  await verifySiteFiles(input, receipt.inputFiles);
  const original = validateSiteIsolatedReceipt(await readJson(path.join(input, SITE_GH.metadataProof), SITE_GH.jsonBytes));
  requireSite(isDeepStrictEqual({ ...receipt, state: SITE_GH.metadataState, archives: null, inputFiles: null }, original));
  const selection = await selectSiteIsolatedEvidence({ input, mode: receipt.mode,
    requestedRun: receipt.mode === SITE_GH.validate ? String(receipt.run.id) : null,
    producer: receipt.mode === SITE_GH.publish ? { runId: receipt.run.id, attempt: receipt.run.attempt,
      sourceRevision: receipt.source.measured } : null });
  const authenticated = await proveSiteIsolatedEvidence({ input, selection, source: { website: receipt.source.website,
    control: receipt.source.control }, mode: receipt.mode });
  requireSite(isDeepStrictEqual(authenticated, original));
  await verifyOriginalSiteEntries(input, receipt);
  const proof = validateAggregateProof(await readJson(path.join(input, 'input', 'provider', 'proof.json'), SITE_GH.jsonBytes), createIsolatedPlan());
  requireSite(isDeepStrictEqual(proof.cohort, receipt.cohort));
  const expected = new Map(receipt.workers.map(worker => [worker.id, worker]));
  for (const item of proof.cells) {
    requireSite(isDeepStrictEqual(item.job, expected.get(item.id)?.job) && isDeepStrictEqual(item.artifact, plainArtifact(expected.get(item.id)?.artifact)));
  }
  const images = await verifyImages(input, receipt);
  const projection = await produceIsolatedProjection({ input: path.join(input, 'input', 'aggregate') });
  requireSite(isDeepStrictEqual(projection.cohort, receipt.cohort));
  for (const worker of projection.workers) {
    const item = proof.cells.find(cell => cell.id === worker.id);
    requireSite(worker.rawSha256 === item.workerSha256 && isDeepStrictEqual(worker.job, item.job) && isDeepStrictEqual(worker.artifact, item.artifact));
    requireWorkerImages({ report: worker.report, worker: { target: worker.target } }, images);
  }
  return { workers: receipt.workers.length, files: receipt.inputFiles.length };
}
