import { parseArguments, validateInvocation } from './functional-coverage.server-image-contracts.mjs';
import { readBoundedRegularFile } from './functional-coverage.server-image-files.mjs';
import { materialize } from './functional-coverage.server-image-materializer.mjs';

function readInvocation(invocationPath, maximumBytes) {
  const input = readBoundedRegularFile(invocationPath, maximumBytes, 'The image invocation file is invalid.');
  if (input.links !== 1 || (input.mode & 0o077) !== 0 || (input.mode & 0o400) === 0) {
    throw new Error('The image invocation file must be an exclusively owned private regular file.');
  }
  let value;
  try {
    value = JSON.parse(input.bytes.toString('utf8'));
  } catch {
    throw new Error('The image invocation JSON is invalid.');
  }
  return validateInvocation(value);
}

async function main() {
  try {
    const parsed = parseArguments(process.argv.slice(2));
    const invocation = readInvocation(parsed.invocationPath, parsed.maximumBytes);
    const result = await materialize(invocation);
    process.stdout.write(`${JSON.stringify(result)}\n`);
  } catch {
    process.stderr.write('Native coverage image materialization failed.\n');
    process.exitCode = 1;
  }
}

await main();
