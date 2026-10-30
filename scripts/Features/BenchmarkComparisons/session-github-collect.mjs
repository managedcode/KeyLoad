import path from 'node:path';
import { link } from 'node:fs/promises';
import { setTimeout as wait } from 'node:timers/promises';
import { pathToFileURL } from 'node:url';
import { createDirectory } from './files.mjs';
import { verifySourceCheckout } from './source-checkout.mjs';
import { captureRun, downloadArtifact } from './isolated-github-api.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { inspectZip, extractEntry } from './isolated-github-zip.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { readJson, writeJson } from './isolated-github-files.mjs';
import { projectArtifact, projectJob } from './isolated-github-validation.mjs';
import { createSessionContext } from './session-github-context.mjs';
import { selectSessionEvidence } from './session-github-selection.mjs';
import { SESSION, requireSession } from './session-contract.mjs';
import { collectSessionRuntimeIdentity, readSessionRuntimeIdentity } from './session-runtime-identity.mjs';
import { validateSessionEnvelope } from './session-report-validation.mjs';

export async function collectSessionWorker(input, selected, context, runtimeIdentity) {
  const { row, job, artifact } = selected;
  const archive = path.join(input, 'archives', `${row.id}.zip`);
  await downloadArtifact(artifact, archive, SESSION.archiveBytes, context);
  await validateDownloadedArchive(archive, artifact, SESSION.archiveBytes);
  const inventory = await inspectZip(archive, ['session.json'],
    path.join(input, 'github', `${row.id}-inventory.txt`), context);
  const directory = await createDirectory(path.join(input, 'workers', row.id));
  const raw = await extractEntry(archive, 'session.json', path.join(directory, 'session.json'), SESSION.sessionBytes, context);
  const envelope = validateSessionEnvelope(await readJson(path.join(directory, 'session.json')),
    row, context.cohort, job.id, runtimeIdentity);
  requireSession(envelope.outcome === 'failure' ? job.conclusion === 'failure' : job.conclusion === 'success');
  const references = [...envelope.cases, envelope.runtimeReport, envelope.outerRuntime, envelope.qualificationReport, ...(envelope.resourceReport ? [envelope.resourceReport] : [])];
  const directories = new Set([directory]);
  for (const reference of references) {
    requireSession(inventory.filter(entry => entry === reference.path).length === 1);
    const target = path.join(directory, reference.path);
    let parent = directory;
    for (const component of reference.path.split('/').slice(0, -1)) {
      parent = path.join(parent, component);
      if (!directories.has(parent)) { await createDirectory(parent); directories.add(parent); }
    }
    const extracted = await extractEntry(archive, reference.path, target,
      reference === envelope.runtimeReport ? SESSION.runtimeBytesCore : SESSION.reportBytes, context);
    requireSession(extracted.sha256 === reference.sha256);
  }
  requireSession(inventory.includes('session-state.json'));
  await extractEntry(archive, 'session-state.json', path.join(directory, 'session-state.json'), SESSION.sessionBytes, context);
  return { id: row.id, target: row.target, nodeCount: row.nodeCount,
    job: projectJob(job, context.cohort, SESSION.workerSteps), artifact: projectArtifact(artifact), sessionSha256: raw.sha256 };
}
async function captureCompleteSessions(input, context) {
  const github = path.join(input, 'github');
  const captures = await createDirectory(path.join(github, 'captures'));
  const deadline = process.hrtime.bigint() + 180_000_000_000n;
  for (let attempt = 1; attempt <= 4; attempt++) {
    const directory = await createDirectory(path.join(captures, String(attempt)));
    const capture = await captureRun(directory, context, true, { deadline });
    const selected = selectSessionEvidence(capture, context.cohort);
    if (selected.workers.length === SESSION.workers) {
      for (const name of ['workflow.json', 'run-attempt.json', 'jobs-pages.json', 'artifacts-pages.json'])
        await link(path.join(directory, name), path.join(github, name));
      return selected;
    }
    process.stdout.write(`GitHub session metadata contains ${selected.workers.length}/${SESSION.workers} workers (capture ${attempt}/4).\n`);
    requireSession(attempt < 4);
    const remaining = Number((deadline - process.hrtime.bigint()) / 1000000n);
    requireSession(remaining > 20_000);
    await wait(20_000);
  }
  requireSession(false);
}
export async function collectSessionEvidence(environment = process.env, argv = process.argv.slice(2)) {
  requireSession(argv.length === 1 && /^--input=.+$/.test(argv[0]));
  const input = path.resolve(argv[0].slice('--input='.length));
  const context = createSessionContext(environment, process.platform);
  await verifySourceCheckout(context.execution);
  await createDirectory(input);
  for (const name of ['github', 'archives', 'workers']) await createDirectory(path.join(input, name));
  await initializeTransport(context, input, path.join(input, 'github'));
  const selected = await captureCompleteSessions(input, context);
  const runtimeIdentity = await collectSessionRuntimeIdentity(input, selected.runtimeArtifact, context);
  const identity = await readSessionRuntimeIdentity(path.join(input, 'github', 'runtime'), context.cohort);
  const workers = [];
  for (const worker of selected.workers) workers.push(await collectSessionWorker(input, worker, context, identity));
  const proof = { schemaVersion: SESSION.schema, cohort: context.cohort, workers,
    preparation: { job: projectJob(selected.preparation, context.cohort, SESSION.prepareSteps),
      compiledArtifact: projectArtifact(selected.compiledArtifact), runtimeIdentity } };
  await writeJson(path.join(input, 'proof.json'), proof);
  return { schemaVersion: SESSION.schema, workers: workers.length };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(JSON.stringify(await collectSessionEvidence()) + '\n'); } catch {
    process.stderr.write(SESSION.failure + '\n');
    process.exitCode = 1;
  }
}
