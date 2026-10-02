import { readFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const ENV = Object.freeze({ repository: 'KEYLOAD_SITE_REPOSITORY', reports: 'KEYLOAD_SITE_REPORTS' });
const MODULES = Object.freeze({ measurements: 'site/Features/BenchmarkComparisons/measurements.mjs', loader: 'site/Features/BenchmarkComparisons/measurement-loader.mjs' });
const OPERATIONS = Object.freeze({ rows: 'rows', median: 'median', catalog: 'validateCatalog', report: 'validateReport', loadReport: 'loadReport', hash: 'hash' });
const FIELDS = Object.freeze({ operation: 'operation', reportPath: 'reportPath', scenario: 'scenario', metric: 'metric', repetition: 'repetition', values: 'values', value: 'value', expectedRevision: 'expectedRevision', filePath: 'filePath', entry: 'entry', baseUrl: 'baseUrl', ok: 'ok', result: 'result', error: 'error' });
const TOKENS = Object.freeze({ utf8: 'utf8', separator: '..', windowsPlatform: 'win32', windowsSeparator: '\\', posixSeparator: '/', invalidJson: 'Probe input exceeds its bounded size.', absolutePath: 'Probe file paths must be absolute.', escape: 'Probe report path escapes its authentic report directory.', missingRepository: 'Repository root environment is missing.', missingReports: 'Authentic report directory environment is missing.', dispatchError: 'Probe operation is not in the frozen protocol.' });
const EXIT = Object.freeze({ failure: 1 });
const LIMITS = Object.freeze({ inputCharacters: 2_000_000 });

async function readInput() {
  let input = '';
  for await (const part of process.stdin) {
    input += part;
    if (input.length > LIMITS.inputCharacters) throw new Error(TOKENS.invalidJson);
  }

  return JSON.parse(input);
}

function requireFileWithin(path, root) {
  if (!isAbsolute(path)) throw new Error(TOKENS.absolutePath);
  const fullPath = resolve(path);
  const distance = relative(root, fullPath);
  if (!distance || distance.startsWith(`${TOKENS.separator}${process.platform === TOKENS.windowsPlatform ? TOKENS.windowsSeparator : TOKENS.posixSeparator}`) || distance === TOKENS.separator || isAbsolute(distance)) {
    throw new Error(TOKENS.escape);
  }

  return fullPath;
}

async function dispatch(request) {
  const repository = process.env[ENV.repository];
  if (!repository || !isAbsolute(repository)) throw new Error(TOKENS.missingRepository);
  const moduleRoot = resolve(repository);
  const measurements = await import(pathToFileURL(resolve(moduleRoot, MODULES.measurements)));
  const operation = request?.[FIELDS.operation];
  if (operation === OPERATIONS.rows) {
    const reports = process.env[ENV.reports];
    if (!reports || !isAbsolute(reports)) throw new Error(TOKENS.missingReports);
    const reportPath = requireFileWithin(request[FIELDS.reportPath], resolve(reports));
    const report = JSON.parse(await readFile(reportPath, TOKENS.utf8));
    return measurements.selectedRows(report, request[FIELDS.scenario], request[FIELDS.repetition], request[FIELDS.metric]);
  }

  if (operation === OPERATIONS.median) return measurements.median(request[FIELDS.values]);
  const loader = await import(pathToFileURL(resolve(moduleRoot, MODULES.loader)));
  if (operation === OPERATIONS.catalog) return loader.validateCatalog(request[FIELDS.value]);
  if (operation === OPERATIONS.report) return loader.validateReport(request[FIELDS.value], request[FIELDS.expectedRevision]);
  if (operation === OPERATIONS.loadReport) return loader.loadReport({ entry: request[FIELDS.entry], baseUrl: request[FIELDS.baseUrl] });
  if (operation === OPERATIONS.hash) return loader.sha256(await readFile(request[FIELDS.filePath]));
  throw new Error(TOKENS.dispatchError);
}

async function main() {
  let request;
  try {
    request = await readInput();
  } catch (error) {
    process.stderr.write(String(error));
    process.exitCode = EXIT.failure;
    return;
  }

  try {
    const result = await dispatch(request);
    process.stdout.write(JSON.stringify({ [FIELDS.ok]: true, [FIELDS.result]: result }));
  } catch (error) {
    if ([OPERATIONS.catalog, OPERATIONS.report, OPERATIONS.loadReport].includes(request?.[FIELDS.operation])) {
      process.stdout.write(JSON.stringify({ [FIELDS.ok]: false, [FIELDS.error]: String(error?.message ?? error) }));
      return;
    }

    process.stderr.write(String(error));
    process.exitCode = EXIT.failure;
  }
}

await main();
