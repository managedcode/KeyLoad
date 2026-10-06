import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { CELL_TERMINAL_ERRORS } from './open-loop-cell-terminal-contract.mjs';
import { aggregateOpenLoopCohort } from './open-loop-cohort-aggregate.mjs';

const KEYS = Object.freeze(['input', 'output']);

function parseArguments(args) {
  const values = {};
  for (const arg of args) {
    const match = /^--([a-z]+)=(.+)$/u.exec(arg);
    if (match === null || !KEYS.includes(match[1]) || Object.hasOwn(values, match[1])) {
      throw new Error(CELL_TERMINAL_ERRORS.input);
    }
    values[match[1]] = match[2];
  }
  if (!KEYS.every(key => Object.hasOwn(values, key))) throw new Error(CELL_TERMINAL_ERRORS.input);
  return values;
}

export async function runOpenLoopCohortCli(args) {
  const result = await aggregateOpenLoopCohort(parseArguments(args));
  return { schemaVersion: result.schemaVersion, qualified: result.qualified, cells: result.cells.length };
}

function isEntryPoint() {
  return process.argv[1] !== undefined && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
}

if (isEntryPoint()) {
  try {
    process.stdout.write(`${JSON.stringify(await runOpenLoopCohortCli(process.argv.slice(2)))}\n`);
  } catch {
    process.stderr.write(`${JSON.stringify({ error: CELL_TERMINAL_ERRORS.cohort })}\n`);
    process.exitCode = 1;
  }
}
