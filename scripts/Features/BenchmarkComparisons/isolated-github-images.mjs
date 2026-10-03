import path from 'node:path';
import { fileName } from './image-contracts.mjs';
import { readPreparedImages } from './image-bundle-read.mjs';
import { bundleFile, bundleLimit } from './image-bundle-contract.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { exactKeys } from './aggregate-contracts.mjs';
import { GH, hashPattern, positive, requireGitHub } from './isolated-github-contract.mjs';
import { downloadArtifact } from './isolated-github-api.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { extractNativeEntry, inspectNativeZip } from './isolated-github-zip.mjs';
import { hashRegularFile, readJson } from './isolated-github-files.mjs';
import { projectArtifact, projectJob } from './isolated-github-validation.mjs';

export const imageJsonFiles = Object.freeze([bundleFile.descriptor, fileName.receipt, fileName.serverManifest, fileName.comparisonsManifest]);

function validateImageDescriptor(bundle, native) {
  requireGitHub(exactKeys(bundle, ['schemaVersion', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'images'])
    && bundle.schemaVersion === 1 && bundle.sourceRevision === native.sourceSha && bundle.runId === native.runId
    && bundle.attempt === native.runAttempt && bundle.repository === native.repository && bundle.ref === native.ref
    && exactKeys(bundle.images, ['server', 'comparisons']));
  for (const name of ['server', 'comparisons']) {
    const record = bundle.images[name];
    requireGitHub(exactKeys(record, ['archive', 'bytes', 'sha256']) && record.archive === bundleFile[name]
      && positive(record.bytes) && record.bytes <= bundleLimit.archiveBytes && hashPattern.test(record.sha256));
  }
}

export async function collectImageEvidence(input, selected, context) {
  const relativeArchive = 'archives/comparison-image-bundle.zip';
  const archive = path.join(input, relativeArchive);
  await downloadArtifact(selected.artifact, archive, GH.imageZipBytes, context);
  const actual = await validateDownloadedArchive(archive, selected.artifact, GH.imageZipBytes);
  const directory = await createDirectory(path.join(input, 'github', 'images'));
  await inspectNativeZip(archive, [...imageJsonFiles, bundleFile.server, bundleFile.comparisons],
    path.join(input, 'github', 'image-zip-inventory.txt'), context);
  const files = [];
  for (const name of imageJsonFiles) {
    const target = path.join(directory, name);
    await extractNativeEntry(archive, name, target, bundleLimit.jsonBytes, context);
    files.push({ path: `github/images/${name}`, ...await hashRegularFile(target, bundleLimit.jsonBytes) });
  }
  const prepared = await readPreparedImages(directory, context.native);
  validateImageDescriptor(await readJson(path.join(directory, bundleFile.descriptor)), context.native);
  return { schemaVersion: 1, cohort: context.cohort, job: projectJob(selected.job, context.cohort, GH.imageSteps),
    artifact: projectArtifact(selected.artifact), archive: { path: relativeArchive, ...actual }, files,
    images: { server: prepared.receipt.images.server.reference, loadGenerator: prepared.receipt.images.comparisons.reference } };
}

export function requireWorkerImages(envelope, images) {
  if (envelope.report === null) return;
  requireGitHub(envelope.report.loadGeneratorImage === images.loadGenerator);
  if (envelope.worker.target === 'KeyLoad') {
    requireGitHub(envelope.report.targets.length === 1 && envelope.report.targets[0].image === images.server);
  }
}
