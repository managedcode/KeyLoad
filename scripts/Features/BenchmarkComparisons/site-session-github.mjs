import path from 'node:path';
import { readdir } from 'node:fs/promises';
import { createDirectory } from './native-files.mjs';
import { captureApi, downloadArtifact } from './isolated-github-api.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { inspectNativeZip, extractNativeEntry } from './isolated-github-zip.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { readJson, writeJson, hashRegularFile } from './isolated-github-files.mjs';
import { GH } from './isolated-github-contract.mjs';
import { flattenPages, projectArtifact, projectJob } from './isolated-github-validation.mjs';
import { SESSION, requireSession, sessionPositive, sessionPath } from './session-contract.mjs';
import { validateSessionAggregate } from './session-aggregate-contract.mjs';
import { sessionSiteContext, parseSessionSiteArguments } from './site-session-context.mjs';
import { SESSION_SITE, selectSessionSiteProducer, requirePublishableSessionMeasurements } from './site-session-selection.mjs';
import { validateSessionSiteReceipt, sessionSuiteFiles } from './site-session-receipt.mjs';

async function capturePages(route, directory, key, context) {
  const pages = [];
  let count = 1;
  for (let page = 1; page <= count; page += 1) {
    const value = await captureApi(`${route}${route.includes('?') ? '&' : '?'}per_page=${GH.pageSize}&page=${page}`,
      path.join(directory, `${key}-page-${page}.json`), false, context);
    requireSession(Number.isSafeInteger(value.total_count) && value.total_count >= 0 && value.total_count <= GH.items
      && Array.isArray(value[key]) && (pages.length === 0 || value.total_count === pages[0].total_count));
    count = Math.max(1, Math.ceil(value.total_count / GH.pageSize));
    requireSession(count <= GH.pages && value[key].length === Math.min(GH.pageSize, value.total_count - (page - 1) * GH.pageSize));
    pages.push(value);
  }
  const records = pages.flatMap(page => page[key]);
  requireSession(new Set(records.map(record => record.id)).size === records.length && records.every(record => sessionPositive(record.id)));
  await writeJson(path.join(directory, `${key}-pages.json`), pages);
  return records;
}
async function snapshotFiles(input) {
  const files = [];
  async function visit(relative, depth) {
    requireSession(depth <= 8);
    for (const entry of await readdir(path.join(input, relative), { withFileTypes: true })) {
      const name = relative + '/' + entry.name;
      requireSession(sessionPath(name) && !entry.isSymbolicLink() && files.length < 10000);
      if (entry.isDirectory()) await visit(name, depth + 1);
      else files.push({ path: name, ...await hashRegularFile(path.join(input, name), SESSION.nativeBytes, true) });
    }
  }
  await visit('metadata', 0);
  return files.sort((left, right) => left.path.localeCompare(right.path));
}
async function captureSelection(input, context) {
  const metadata = path.join(input, 'metadata');
  const workflow = await captureApi(`${GH.api}/workflows/benchmarks.yml`, path.join(metadata, 'workflow.json'), false, context);
  requireSession(workflow?.name === SESSION.workflow && workflow.path === GH.workflowPath && workflow.state === 'active');
  const runs = context.requestedRun === null
    ? await capturePages(`${GH.api}/workflows/benchmarks.yml/runs?branch=main&status=completed`, metadata, 'workflow_runs', context)
    : [await captureApi(`${GH.api}/runs/${context.requestedRun}`, path.join(metadata, 'requested-run.json'), false, context)];
  const candidates = runs.filter(run => context.mode === 'publish' ? run.conclusion === 'success' : ['success', 'failure'].includes(run.conclusion))
    .sort((left, right) => right.run_number - left.run_number);
  for (const summary of candidates) {
    const directory = await createDirectory(path.join(metadata, String(summary.id)));
    const route = `${GH.api}/runs/${summary.id}/attempts/${summary.run_attempt}`;
    const run = await captureApi(route, path.join(directory, 'run-attempt.json'), false, context);
    requireSession(run.id === summary.id && run.run_attempt === summary.run_attempt && run.head_sha === summary.head_sha
      && run.run_number === summary.run_number && run.event === summary.event && run.conclusion === summary.conclusion);
    const jobs = await capturePages(`${route}/jobs`, directory, 'jobs', context);
    const artifacts = await capturePages(`${GH.api}/runs/${run.id}/artifacts`, directory, 'artifacts', context);
    const selected = selectSessionSiteProducer({ run, jobs, artifacts }, workflow, context.mode);
    if (selected !== null) return { ...selected, run, workflow, directory, capture: {run,jobs,artifacts} };
    requireSession(context.optional);
  }
  requireSession(context.optional);
  return null;
}
async function downloadOriginal(artifact, input, name, maximum, context) {
  const archive = path.join(input, 'archives', name);
  await downloadArtifact(artifact, archive, maximum, context);
  await validateDownloadedArchive(archive, artifact, maximum);
  return { path: 'archives/' + name, ...await hashRegularFile(archive, maximum) };
}
export async function captureSessionSite({ environment = process.env, args, download = true }) {
  const values = parseSessionSiteArguments(args,
    ['input', 'mode', 'site-revision', 'workflow-revision', 'requested-run', 'optional'],
    ['input', 'mode', 'site-revision', 'workflow-revision']);
  const context = sessionSiteContext(environment, values);
  const input = values.input;
  await createDirectory(input);
  const metadata = await createDirectory(path.join(input, 'metadata'));
  await initializeTransport(context, input, metadata);
  const selected = await captureSelection(input, context);
  if (selected === null) {
    const absent = validateSessionSiteReceipt({ schemaVersion: SESSION.schema, state: 'unavailable', mode: context.mode,
      publishEligible: context.mode === 'publish', source: context.source, producer: null, metadataFiles: await snapshotFiles(input) });
    await writeJson(path.join(input, 'metadata-proof.json'), absent);
    return absent;
  }
  if (!download) {
    const fresh = { schemaVersion: SESSION.schema, state: 'selection_verified', source: { ...context.source, measured: selected.cohort.sourceRevision },
      cohort: selected.cohort, aggregateJobId: selected.job.id, suiteArtifactId: selected.suite.id,
      providerArtifactId: selected.provider.id,
      preparation: {jobId:selected.native.preparation.id,runtimeArtifactId:selected.native.runtimeArtifact.id,
        runtimeDigest:selected.native.runtimeArtifact.digest,compiledArtifactId:selected.native.compiledArtifact.id,
        compiledDigest:selected.native.compiledArtifact.digest,functionalArtifactId:selected.functional.artifact.id,
        functionalDigest:selected.functional.artifact.digest},
      workers: selected.native.workers.map(({row,job,artifact}) => ({id:row.id,jobId:job.id,artifactId:artifact.id,digest:artifact.digest})),
      metadataFiles: await snapshotFiles(input) };
    await writeJson(path.join(input, 'metadata-proof.json'), fresh);
    return fresh;
  }
  await createDirectory(path.join(input, 'archives'));
  const suite = await downloadOriginal(selected.suite, input, SESSION.aggregateArtifact + '.zip', SESSION_SITE.suiteBytes, context);
  await downloadOriginal(selected.provider, input, SESSION_SITE.provider + '.zip', SESSION_SITE.providerBytes, context);
  const functionalSelection = selected.functional;
  const functionalArchive = await downloadOriginal(functionalSelection.artifact, input,
    'keyload-functional-heavy-load.zip', GH.workerZipBytes, context);
  const runtimeArchive = await downloadOriginal(selected.native.runtimeArtifact, input,
    'comparison-compiled-runtime-identity.zip', 134_217_728, context);
  await inspectNativeZip(path.join(input, suite.path), ['aggregate.json'], path.join(metadata, 'suite-inventory.txt'), context);
  const aggregateRaw = await extractNativeEntry(path.join(input, suite.path), 'aggregate.json',
    path.join(metadata, 'aggregate-original.json'), SESSION.aggregateBytes, context);
  const aggregate = validateSessionAggregate(await readJson(path.join(metadata, 'aggregate-original.json'), SESSION.aggregateBytes));
  if (context.mode === 'publish') requirePublishableSessionMeasurements(aggregate);
  requireSession(JSON.stringify(aggregate.cohort) === JSON.stringify(selected.cohort));
  const workerArchives = [];
  const workers = selected.native.workers.map(({ row, job, artifact }) => ({ id: row.id, target: row.target, nodeCount: row.nodeCount,
    job: projectJob(job, selected.cohort, SESSION.workerSteps), artifact: projectArtifact(artifact) }));
  for (const [index, worker] of selected.native.workers.entries()) {
    const original = await downloadOriginal(worker.artifact, input, worker.row.artifactName + '.zip', SESSION.archiveBytes, context);
    workerArchives.push({ id: worker.row.id, ...original });
    const measured = aggregate.workers[index];
    requireSession(measured.job.id === worker.job.id && measured.artifact.id === worker.artifact.id
      && measured.artifact.digest === worker.artifact.digest);
  }
  const receipt = validateSessionSiteReceipt({ schemaVersion: SESSION.schema, state: 'metadata_verified', mode: context.mode,
    publishEligible: context.mode === 'publish', source: { ...context.source, measured: selected.cohort.sourceRevision }, producer: null,
    repository: { id: selected.run.repository.id, fullName: SESSION.repository },
    workflow: { id: selected.workflow.id, path: GH.workflowPath },
    run: { id: selected.run.id, number: selected.run.run_number, attempt: selected.run.run_attempt,
      url: `https://github.com/${SESSION.repository}/actions/runs/${selected.run.id}` }, cohort: selected.cohort,
    aggregateJob: projectJob(selected.job, selected.cohort, SESSION_SITE.aggregateSteps),
    artifacts: { suite: projectArtifact(selected.suite), provider: projectArtifact(selected.provider) }, workers,
    preparation: { job: projectJob(selected.native.preparation, selected.cohort, SESSION.prepareSteps),
      compiledArtifact: projectArtifact(selected.native.compiledArtifact) },
    functional: { artifact: projectArtifact(functionalSelection.artifact), archive: functionalArchive },
    runtimeIdentity: { artifact: projectArtifact(selected.native.runtimeArtifact), archive: runtimeArchive },
    suiteFiles: sessionSuiteFiles(aggregate, aggregateRaw.sha256), workerArchives,
    archives: null, inputFiles: null, metadataFiles: await snapshotFiles(input) });
  await writeJson(path.join(input, 'metadata-proof.json'), receipt);
  return receipt;
}
