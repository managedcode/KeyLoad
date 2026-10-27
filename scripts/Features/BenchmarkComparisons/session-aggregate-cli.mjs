import path from 'node:path';
import { aggregateSessions } from './session-aggregate.mjs';
import { SESSION, requireSession } from './session-contract.mjs';
try {
  const args = new Map();
  for (const argument of process.argv.slice(2)) {
    const match = /^--(input|output)=(.+)$/.exec(argument);
    requireSession(match !== null && !args.has(match[1]) && path.isAbsolute(match[2]));
    args.set(match[1], path.resolve(match[2]));
  }
  requireSession(args.size === 2);
  const result = await aggregateSessions({ ...Object.fromEntries(args), resourcePolicy: 'comparison' });
  process.stdout.write(JSON.stringify({ schemaVersion: result.schemaVersion, workers: result.workers.length }) + '\n');
} catch {
  process.stderr.write(SESSION.failure + '\n');
  process.exitCode = 1;
}
