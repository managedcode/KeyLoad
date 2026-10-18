import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { constants } from 'node:fs';
import { open } from 'node:fs/promises';
import { requireOutputFile } from './native-files.mjs';
import { writeJson } from './isolated-github-files.mjs';
import { SESSION, SESSION_TARGETS, requireSession, sessionRows } from './session-contract.mjs';

export { createSessionPlan, validateSessionPlan } from './session-plan-contract.mjs';
import { createSessionPlan } from './session-plan-contract.mjs';
export function sessionMatrices() {
  const rows = sessionRows();
  return Object.fromEntries(SESSION_TARGETS.map(target => [target.key,
    { include: rows.filter(row => row.target === target.name) }]));
}
export async function writeSessionPlan(argv) {
  requireSession(Array.isArray(argv) && argv.length === 2);
  const args = new Map();
  for (const argument of argv) {
    const match = /^--(output|github-output)=(.+)$/.exec(argument);
    requireSession(match !== null && !args.has(match[1]) && path.isAbsolute(match[2]));
    args.set(match[1], match[2]);
  }
  requireSession(args.size === 2);
  await requireOutputFile(args.get('github-output'));
  const plan = createSessionPlan();
  await writeJson(args.get('output'), plan);
  const handle = await open(args.get('github-output'), constants.O_WRONLY | constants.O_APPEND | (constants.O_NOFOLLOW ?? 0));
  try {
    await handle.writeFile(`database_matrices=${JSON.stringify(sessionMatrices())}\nsession_matrix=${JSON.stringify({ include: plan.workers })}\n`);
    await handle.sync();
  } finally { await handle.close(); }
  return plan;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await writeSessionPlan(process.argv.slice(2)); } catch {
    process.stderr.write(SESSION.failure + '\n');
    process.exitCode = 1;
  }
}
