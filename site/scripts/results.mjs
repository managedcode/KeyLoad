import { mergeResults } from '../Features/BenchmarkComparisons/results-merge.mjs';

const NAMES = new Set(['--input', '--output']);

function parse(argv) {
  const args = {};
  for (const item of argv) {
    const separator = item.indexOf('=');
    const name = separator < 0 ? item : item.slice(0, separator);
    if (separator < 0 || !NAMES.has(name) || name in args) throw new Error('Use --input=<directory> --output=<results.json>.');
    args[name] = item.slice(separator + 1);
  }
  if (!args['--input'] || !args['--output']) throw new Error('Use --input=<directory> --output=<results.json>.');
  return { input: args['--input'], output: args['--output'] };
}

try {
  console.log(JSON.stringify(await mergeResults(parse(process.argv.slice(2)))));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
