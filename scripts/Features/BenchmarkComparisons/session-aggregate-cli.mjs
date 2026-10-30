import path from 'node:path';
import { aggregateSessions } from './session-aggregate.mjs';
import { SESSION, requireSession } from './session-contract.mjs';
import { writeJson } from './isolated-github-files.mjs';
try {
  const args = new Map();
  for (const argument of process.argv.slice(2)) {
    const match = /^--(input|output|resource-policy)=(.+)$/.exec(argument);
    requireSession(match !== null && !args.has(match[1]));
    if (match[1] === 'resource-policy') {
      requireSession(['comparison', 'recorded'].includes(match[2]));
      args.set(match[1], match[2]);
    } else {
      requireSession(path.isAbsolute(match[2]));
      args.set(match[1], path.resolve(match[2]));
    }
  }
  requireSession(args.has('input') && args.has('output'));
  const resourcePolicy = args.get('resource-policy') ?? 'comparison';
  const result = await aggregateSessions({ input: args.get('input'), output: args.get('output'), resourcePolicy });
  requireSession(result.workers.length === SESSION.workers);
  await writeJson(path.join(args.get('output'), 'resource-policy.json'), { resourcePolicy });
  process.stdout.write(JSON.stringify({ schemaVersion: result.schemaVersion, workers: result.workers.length,
    resourcePolicy }) + '\n');
} catch {
  process.stderr.write(SESSION.failure + '\n');
  process.exitCode = 1;
}
