import path from 'node:path';
import { readIsolatedContract } from './isolated-plan.mjs';
import { requireWorkerJobAgreement, validateWorkerEnvelope } from './aggregate-validation.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { downloadArtifact } from './isolated-github-api.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { extractNativeEntry, inspectNativeZip } from './isolated-github-zip.mjs';
import { readJson } from './isolated-github-files.mjs';
import { projectWorkerProof } from './isolated-github-validation.mjs';
import { requireWorkerImages } from './isolated-github-images.mjs';

export async function collectWorkerEvidence(input, selected, images, context) {
  const { cell, artifact, job } = selected;
  const archive = path.join(input, 'archives', `${cell.id}.zip`);
  await downloadArtifact(artifact, archive, GH.workerZipBytes, context);
  await validateDownloadedArchive(archive, artifact, GH.workerZipBytes);
  await inspectNativeZip(archive, ['worker.json'], path.join(input, 'github', `${cell.id}-zip-inventory.txt`), context);
  const directory = await createDirectory(path.join(input, 'data', 'workers', cell.id));
  const target = path.join(directory, 'worker.json');
  const raw = await extractNativeEntry(archive, 'worker.json', target, GH.workerRawBytes, context);
  const envelope = validateWorkerEnvelope(await readJson(target, GH.workerRawBytes), cell, context.cohort, readIsolatedContract());
  requireGitHub(envelope.worker.jobId === job.id);
  requireWorkerJobAgreement(envelope, job);
  requireWorkerImages(envelope, images);
  return projectWorkerProof(job, artifact, cell, context.cohort, raw.sha256);
}
