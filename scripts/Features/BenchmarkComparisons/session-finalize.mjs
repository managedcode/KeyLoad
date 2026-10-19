import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { readJson } from './isolated-github-files.mjs';
import { readOriginalSession } from './session-aggregate.mjs';
import { hashRegularFile } from './isolated-github-files.mjs';
import { createSessionContext, selectSession } from './session-github-context.mjs';
import { SESSION, requireSession } from './session-contract.mjs';

// Execution fixture outcomes are retained verbatim. Missing execution output is never manufactured as unavailable.
export async function finalizeSession(environment = process.env, argv = process.argv.slice(2)) {
  requireSession(argv.length === 0);
  const context = createSessionContext(environment, process.platform);
  const row = selectSession(environment);
  requireSession(/^[1-9]\d*$/.test(environment.KEYLOAD_COMPARISON_JOB_ID ?? '')
    && ['success', 'failure'].includes(environment.KEYLOAD_WORKLOAD_OUTCOME));
  const jobId = Number(environment.KEYLOAD_COMPARISON_JOB_ID);
  const relative = `artifacts/comparisons/sessions/workers/${row.id}`;
  const directory = path.join(context.execution.workspace, relative);
  requireSession(path.resolve(context.execution.workspace, environment.KEYLOAD_COMPARISON_EVIDENCE_DIRECTORY ?? '')
    === path.resolve(directory));
  const session = await readJson(path.join(directory, 'session.json'), SESSION.sessionBytes);
  const raw = await hashRegularFile(path.join(directory, 'session.json'), SESSION.sessionBytes);
  const metadata = { job: { id: jobId, conclusion: environment.KEYLOAD_WORKLOAD_OUTCOME }, sessionSha256: raw.sha256 };
  await readOriginalSession(directory, row, context.cohort, metadata);
  requireSession(session.jobId === jobId);
  return { schemaVersion: SESSION.schema, id: row.id, directory: relative, outcome: session.outcome };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(JSON.stringify(await finalizeSession()) + '\n'); } catch {
    process.stderr.write(SESSION.failure + '\n');
    process.exitCode = 1;
  }
}
