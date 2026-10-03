import { pathToFileURL } from 'node:url';
import { isAbsolute, resolve } from 'node:path';

const LIMIT = 2_000_000;
const OPERATIONS = Object.freeze({ median: 'median' });
const readRequest = async () => {
  let input = '';
  for await (const part of process.stdin) {
    input += part;
    if (input.length > LIMIT) throw new Error('Probe input exceeds its bounded size.');
  }
  return JSON.parse(input);
};

try {
  const request = await readRequest();
  const repository = process.env.KEYLOAD_SITE_REPOSITORY;
  if (!repository || !isAbsolute(repository)) throw new Error('Repository root environment is missing.');
  if (request?.operation !== OPERATIONS.median || !Array.isArray(request.values)) {
    throw new Error('Probe operation is not the supported current arithmetic operation.');
  }
  const modulePath = resolve(repository, 'site/Features/BenchmarkComparisons/measurements.mjs');
  const measurements = await import(pathToFileURL(modulePath));
  process.stdout.write(JSON.stringify({ result: measurements.median(request.values) }));
} catch (error) {
  process.stderr.write(String(error?.message ?? error));
  process.exitCode = 1;
}
