import { constants } from 'node:fs';
import { open } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { verifySourceCheckout } from './prepare-images.mjs';
import { createDirectory, requireOutputFile } from './image-bundle-files.mjs';
import { captureCurrentJobPages, validateKnownCurrentJob } from './isolated-github-api.mjs';
import { captureFreshCurrentJob } from './isolated-current-job.mjs';
import { writeJson } from './isolated-github-files.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { createSessionContext, selectSession } from './session-github-context.mjs';
import { SESSION, requireSession } from './session-contract.mjs';

export async function captureSessionJob(environment = process.env, argv = process.argv.slice(2)) {
  requireSession(Array.isArray(argv) && argv.length === 0);
  const context = createSessionContext(environment, process.platform);
  const row = environment.KEYLOAD_COMPARISON_JOB_NAME === SESSION.prepareJob
    ? { jobName: SESSION.prepareJob } : selectSession(environment);
  requireSession(typeof environment.GITHUB_ENV === 'string' && path.isAbsolute(environment.GITHUB_ENV));
  await requireOutputFile(environment.GITHUB_ENV);
  await verifySourceCheckout(context.native);
  const directory = await createDirectory(path.join(context.native.runnerTemp, 'keyload-session-github'));
  await initializeTransport(context, directory, directory);
  const discovered = await captureCurrentJobPages(directory, context, row.jobName);
  const job = await captureFreshCurrentJob(discovered, directory, context, row.jobName);
  validateKnownCurrentJob(job, context.cohort, row.jobName, discovered.id);
  await writeJson(path.join(directory, 'job.json'), job);
  await writeJson(path.join(directory, 'cohort.json'), context.cohort);
  const handle = await open(environment.GITHUB_ENV, constants.O_WRONLY | constants.O_APPEND | (constants.O_NOFOLLOW ?? 0));
  try {
    const evidence = row.id === undefined ? '' : `KEYLOAD_COMPARISON_EVIDENCE_DIRECTORY=${path.join(context.native.workspace, 'artifacts/comparisons/sessions/workers', row.id)}\n`;
    await handle.writeFile(`KEYLOAD_COMPARISON_JOB_ID=${job.id}\n${evidence}`);
    await handle.sync();
  } finally { await handle.close(); }
  return job;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await captureSessionJob(); } catch {
    process.stderr.write(SESSION.failure + '\n');
    process.exitCode = 1;
  }
}
