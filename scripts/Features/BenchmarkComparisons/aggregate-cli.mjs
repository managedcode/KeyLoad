import { aggregateEvidence } from './aggregate-evidence.mjs';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';

const ARGUMENTS = Object.freeze(['input', 'output', 'plan', 'proof']);

function parseArguments(arguments_) {
  const result = {};
  for (const argument of arguments_) {
    const match = /^--([a-z]+)=(.+)$/.exec(argument);
    requireValue(match !== null && ARGUMENTS.includes(match[1]) && !Object.hasOwn(result, match[1]), AGGREGATE.errors.input);
    result[match[1]] = match[2];
  }
  requireValue(ARGUMENTS.every(key => Object.hasOwn(result, key)), AGGREGATE.errors.input);
  return result;
}

try {
  const result = await aggregateEvidence(parseArguments(process.argv.slice(2)));
  process.stdout.write(JSON.stringify({ schemaVersion: result.schemaVersion, workers: result.workers.length }) + '\n');
} catch (error) {
  const known = Object.values(AGGREGATE.errors).includes(error.code);
  process.stderr.write(JSON.stringify({ error: known ? error.code : AGGREGATE.errors.input }) + '\n');
  process.exitCode = 1;
}
