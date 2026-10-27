import path from 'node:path';
import { link, readdir } from 'node:fs/promises';
import { createDirectory } from './files.mjs';
import { captureApi } from './isolated-github-api.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { writeJson, hashRegularFile } from './isolated-github-files.mjs';
import { GH } from './isolated-github-contract.mjs';
import { projectArtifact, projectJob } from './isolated-github-validation.mjs';
import { SESSION, requireSession, sessionPositive, sessionPath } from './session-contract.mjs';
import { aggregateSessions } from './session-aggregate.mjs';
import { collectSessionWorker } from './session-github-collect.mjs';
import { collectSessionRuntimeIdentity, readSessionRuntimeIdentity } from './session-runtime-identity.mjs';
import { sessionSiteContext, parseSessionSiteArguments } from './site-session-context.mjs';
import { selectSessionSiteProducer, requirePublishableSessionMeasurements } from './site-session-selection.mjs';
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
async function snapshotFiles(input, base = 'metadata', published = base) {
  const files = [];
  async function visit(relative, depth) {
    requireSession(depth <= 8);
    for (const entry of await readdir(path.join(input, relative), { withFileTypes: true })) {
      const name = relative + '/' + entry.name;
      requireSession(sessionPath(name) && !entry.isSymbolicLink() && files.length < 10000);
      if (entry.isDirectory()) await visit(name, depth + 1);
      else files.push({ path: published + name.slice(base.length), ...await hashRegularFile(path.join(input, name), SESSION.reportBytes, true) });
    }
  }
  await visit(base, 0);
  requireSession(files.reduce((sum, file) => sum + file.bytes, 0) <= SESSION.allArchiveBytes);
  return files.sort((left, right) => left.path.localeCompare(right.path));
}
async function captureSelection(input, context) {
  const metadata = path.join(input, 'metadata');
  const workflow = await captureApi(`${GH.api}/workflows/benchmarks.yml`, path.join(metadata, 'workflow.json'), false, context);
  requireSession(workflow?.name === SESSION.workflow && workflow.path === GH.workflowPath && workflow.state === 'active');
  const runs = context.requestedRun === null
    ? await capturePages(`${GH.api}/workflows/benchmarks.yml/runs?branch=main&status=completed`, metadata, 'workflow_runs', context)
    : [await captureApi(`${GH.api}/runs/${context.requestedRun}`, path.join(metadata, 'requested-run.json'), false, context)];
  const candidates = runs.filter(run => ['success', 'failure'].includes(run.conclusion))
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
      cohort: selected.cohort,
      preparation: {jobId:selected.execution.preparation.id,runtimeArtifactId:selected.execution.runtimeArtifact.id,
        runtimeDigest:selected.execution.runtimeArtifact.digest,compiledArtifactId:selected.execution.compiledArtifact.id,
        compiledDigest:selected.execution.compiledArtifact.digest},
      workers: selected.execution.workers.map(({row,job,artifact}) => ({id:row.id,jobId:job.id,artifactId:artifact.id,digest:artifact.digest})),
      metadataFiles: await snapshotFiles(input) };
    await writeJson(path.join(input, 'metadata-proof.json'), fresh);
    return fresh;
  }
  context.cohort = selected.cohort;
  const github = await createDirectory(path.join(input, 'github'));
  for (const name of ['archives', 'workers']) await createDirectory(path.join(input, name));
  await link(path.join(metadata, 'workflow.json'), path.join(github, 'workflow.json'));
  for (const name of ['run-attempt.json', 'jobs-pages.json', 'artifacts-pages.json'])
    await link(path.join(selected.directory, name), path.join(github, name));
  const runtimeIdentity = await collectSessionRuntimeIdentity(input, selected.execution.runtimeArtifact, context);
  const identity = await readSessionRuntimeIdentity(path.join(github, 'runtime'), selected.cohort);
  const workers = [];
  for (const worker of selected.execution.workers)
    workers.push(await collectSessionWorker(input, worker, context, identity));
  const preparation = { job: projectJob(selected.execution.preparation, selected.cohort, SESSION.prepareSteps),
    compiledArtifact: projectArtifact(selected.execution.compiledArtifact), runtimeIdentity };
  await writeJson(path.join(input, 'proof.json'), { schemaVersion: SESSION.schema, cohort: selected.cohort, workers, preparation });
  const prepared = await createDirectory(path.join(input, 'prepared-input'));
  const aggregate = requirePublishableSessionMeasurements(await aggregateSessions({ input,
    output: path.join(prepared, 'aggregate'), resourcePolicy: 'recorded' }));
  const aggregateRaw = await hashRegularFile(path.join(prepared, 'aggregate', 'aggregate.json'), SESSION.aggregateBytes);
  const provider = await createDirectory(path.join(prepared, 'provider'));
  const providerGithub = await createDirectory(path.join(provider, 'github'));
  await createDirectory(path.join(providerGithub, 'runtime'));
  for (const relative of ['proof.json', ...['workflow.json', 'run-attempt.json', 'jobs-pages.json', 'artifacts-pages.json']
    .map(name => 'github/' + name), ...runtimeIdentity.files.map(file => file.path)]) {
    const target = path.join(provider, relative);
    await link(path.join(input, relative), target);
  }
  const workerArchives = [];
  for (const worker of workers) {
    const relative = `archives/${worker.id}.zip`;
    workerArchives.push({ id: worker.id, path: relative, ...await hashRegularFile(path.join(input, relative), SESSION.archiveBytes) });
  }
  const receipt = validateSessionSiteReceipt({ schemaVersion: SESSION.schema, state: 'metadata_verified', mode: context.mode,
    publishEligible: context.mode === 'publish', source: { ...context.source, measured: selected.cohort.sourceRevision }, producer: null,
    repository: { id: selected.run.repository.id, fullName: SESSION.repository },
    workflow: { id: selected.workflow.id, path: GH.workflowPath },
    run: { id: selected.run.id, number: selected.run.run_number, attempt: selected.run.run_attempt,
      url: `https://github.com/${SESSION.repository}/actions/runs/${selected.run.id}` }, cohort: selected.cohort,
    workers,
    preparation: { job: projectJob(selected.execution.preparation, selected.cohort, SESSION.prepareSteps),
      compiledArtifact: projectArtifact(selected.execution.compiledArtifact) },
    runtimeIdentity: { artifact: runtimeIdentity.artifact, archive: runtimeIdentity.archive },
    suiteFiles: sessionSuiteFiles(aggregate, aggregateRaw.sha256), workerArchives,
    inputFiles: await snapshotFiles(input, 'prepared-input', 'input'), metadataFiles: await snapshotFiles(input) });
  await writeJson(path.join(input, 'metadata-proof.json'), receipt);
  return receipt;
}
