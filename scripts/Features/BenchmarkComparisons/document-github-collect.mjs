import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { verifySourceCheckout } from './prepare-images.mjs';
import { collectImageEvidence } from './isolated-github-images.mjs';
import { selectCompletedEvidence } from './isolated-github-selection.mjs';
import { pathToFileURL } from 'node:url';
import { absolutePath } from './aggregate-files.mjs';
import { DOCUMENT, documentJobName, validateDocumentPlan } from './document-isolated-plan.mjs';
import { validateDocumentEnvelope } from './document-evidence.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { createGitHubContext, contextForProfile } from './isolated-github-context.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { flattenPages, validateWorkflowRun, uniqueNamed, validateWorkerJob, validateArtifact, projectWorkerProof } from './isolated-github-validation.mjs';
import { captureRun, downloadArtifact } from './isolated-github-api.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { inspectNativeZip, extractNativeEntry } from './isolated-github-zip.mjs';
import { readJson, writeJson } from './isolated-github-files.mjs';
import { validateServerResourceEvidence } from './server-resource-evidence.mjs';
import { collectDocumentFunctionalGate } from './document-functional-gate.mjs';

function argumentsOf(argv) {
  const values = new Map();
  for (const argument of argv) {
    const match = /^--(capture|plan|output)=(.+)$/u.exec(argument);
    requireGitHub(match !== null && !values.has(match[1]));
    values.set(match[1], absolutePath(match[2]));
  }
  requireGitHub(values.size === 3);
  return Object.fromEntries(values);
}
export function selectDocumentWorkers(capture, context, plan) {
  const names = new Set(plan.cells.map(documentJobName));
  const artifacts = capture.artifacts.filter(item => item.name.startsWith(DOCUMENT.artifactPrefix));
  const jobs = capture.jobs.filter(item => item.name.includes(' / Documents / '));
  requireGitHub(artifacts.length === plan.cells.length && jobs.length === plan.cells.length && jobs.every(job => names.has(job.name)));
  const selected = plan.cells.map(cell => {
    const cohort = contextForProfile(context, cell.profile).cohort;
    const job = validateWorkerJob(uniqueNamed(jobs, documentJobName(cell)), cohort, documentJobName(cell));
    const artifact = validateArtifact(uniqueNamed(artifacts, DOCUMENT.artifactPrefix + cell.id), capture.run, job,
      DOCUMENT.artifactPrefix + cell.id, GH.workerZipBytes);
    return { cell, cohort, job, artifact };
  });
  requireGitHub(new Set(selected.map(item => item.job.id)).size === plan.cells.length
    && new Set(selected.map(item => item.artifact.id)).size === plan.cells.length
    && selected.reduce((bytes, item) => bytes + item.artifact.size_in_bytes, 0) <= GH.totalWorkerZipBytes);
  return selected;
}
export async function collectDocumentGitHubEvidence(environment = process.env, argv = process.argv.slice(2)) {
  const paths = argumentsOf(argv);
  const outside = (from, to) => { const relative = path.relative(from, to); return relative === '..' || relative.startsWith('..' + path.sep); };
  requireGitHub(outside(paths.capture, paths.output) && outside(paths.output, paths.capture));
  const context = createGitHubContext(environment, process.platform);
  await verifySourceCheckout(context.native);
  const plan = validateDocumentPlan(await readJson(paths.plan, GH.metadataBytes));
  const workflow = await readJson(path.join(paths.capture, 'workflow.json'), GH.metadataBytes);
  const run = await readJson(path.join(paths.capture, 'run-attempt.json'), GH.metadataBytes);
  validateWorkflowRun(workflow, run, context.cohort);
  const capture = { run, jobs: flattenPages(await readJson(path.join(paths.capture, 'jobs-pages.json'), GH.metadataBytes), 'jobs'),
    artifacts: flattenPages(await readJson(path.join(paths.capture, 'artifacts-pages.json'), GH.metadataBytes), 'artifacts') };
  const supplied = selectDocumentWorkers(capture, context, plan);
  const output = await createDirectory(paths.output);
  const archiveRoot = await createDirectory(path.join(output, 'archives'));
  const workers = await createDirectory(path.join(output, 'workers'));
  const transport = await createDirectory(path.join(output, 'github'));
  await initializeTransport(context, output, transport);
  const authenticated = await captureRun(transport, context, true);
  const selected = selectDocumentWorkers(authenticated, context, plan);
  requireGitHub(isDeepStrictEqual(supplied, selected));
  const imageSelection = selectCompletedEvidence(authenticated, context, context.plan, context.scaledPlans, context.vectorPlans).image;
  const image = await collectImageEvidence(output, imageSelection, context);
  await writeJson(path.join(transport, 'image-proof.json'), image);
  const functionalGate = await collectDocumentFunctionalGate(output, authenticated, context, image.images.server);
  const cells = [];
  for (const item of selected) {
    const archive = path.join(archiveRoot, item.cell.id + '.zip');
    await downloadArtifact(item.artifact, archive, GH.workerZipBytes, context);
    await validateDownloadedArchive(archive, item.artifact, GH.workerZipBytes);
    await inspectNativeZip(archive, [DOCUMENT.raw, 'server-resource-evidence.json'], path.join(transport, item.cell.id + '-inventory.txt'), context);
    const directory = await createDirectory(path.join(workers, item.cell.id));
    const rawPath = path.join(directory, DOCUMENT.raw);
    const raw = await extractNativeEntry(archive, DOCUMENT.raw, rawPath, GH.workerRawBytes, context);
    const envelope = validateDocumentEnvelope(await readJson(rawPath, GH.workerRawBytes), item.cell, item.cohort);
    requireGitHub(envelope.worker.jobId === item.job.id
      && ((envelope.disposition === 'failed') === (item.job.conclusion === 'failure')));
    if (envelope.report !== null && item.cell.target === 'KeyLoad') {
      requireGitHub(envelope.report.repetitions.every(repetition => repetition.target.image === image.images.server));
    }
    const sidecarPath = path.join(directory, 'server-resource-evidence.json');
    const resourceRaw = await extractNativeEntry(archive, 'server-resource-evidence.json', sidecarPath, GH.serverResourceBytes, context);
    const resource = validateServerResourceEvidence(await readJson(sidecarPath, GH.serverResourceBytes), resourceRaw.sha256,
      raw.sha256, item.cell, item.cohort, item.job.id, envelope.disposition !== 'unsupportedTopology');
    cells.push({ cell: item.cell, proof: projectWorkerProof(item.job, item.artifact, item.cell, item.cohort, raw.sha256, resource),
      rawPath: `workers/${item.cell.id}/${DOCUMENT.raw}`, resourcePath: `workers/${item.cell.id}/server-resource-evidence.json` });
  }
  await writeJson(path.join(output, 'document-plan.json'), plan);
  await writeJson(path.join(output, 'document-intake.json'), { schemaVersion: 1, family: DOCUMENT.family, cohort: context.cohort, functionalGate, cells });
  return cells.length;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await collectDocumentGitHubEvidence(); } catch { process.stderr.write(`${GH.failure}\n`); process.exitCode = 1; }
}
