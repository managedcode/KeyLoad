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
import { validateServerResourceEvidence } from './server-resource-evidence.mjs';

export async function collectWorkerEvidence(captureRoot, dataRoot, selected, images, context, plan) {
  const { cell, artifact, job } = selected;
  const archive = path.join(captureRoot, 'archives', `${cell.id}.zip`);
  await downloadArtifact(artifact, archive, GH.workerZipBytes, context);
  await validateDownloadedArchive(archive, artifact, GH.workerZipBytes);
  const scaled = plan.profile !== readIsolatedContract().profile;
  const entries = scaled ? ['worker.json', 'server-resource-evidence.json'] : ['worker.json'];
  await inspectNativeZip(archive, entries, path.join(captureRoot, 'github', `${cell.id}-zip-inventory.txt`), context);
  const directory = await createDirectory(path.join(dataRoot, 'workers', cell.id));
  const target = path.join(directory, 'worker.json');
  const raw = await extractNativeEntry(archive, 'worker.json', target, GH.workerRawBytes, context);
  const canonical = readIsolatedContract();
  const profileContract = plan.profile === canonical.profile ? canonical : { ...canonical,
    profile: plan.profile, options: plan.profileSettings };
  const envelope = validateWorkerEnvelope(await readJson(target, GH.workerRawBytes), cell, context.cohort, profileContract);
  requireGitHub(envelope.worker.jobId === job.id);
  requireWorkerJobAgreement(envelope, job);
  requireWorkerImages(envelope, images);
  if (!scaled) return projectWorkerProof(job, artifact, cell, context.cohort, raw.sha256);
  const resourcePath = path.join(directory, 'server-resource-evidence.json');
  const resourceRaw = await extractNativeEntry(archive, 'server-resource-evidence.json', resourcePath,
    GH.serverResourceBytes, context);
  const supported = envelope.disposition === 'measured';
  const serverResource = validateServerResourceEvidence(await readJson(resourcePath, GH.serverResourceBytes),
    resourceRaw.sha256, raw.sha256, cell, context.cohort, job.id, supported);
  return projectWorkerProof(job, artifact, cell, context.cohort, raw.sha256, serverResource);
}
