import { spawn } from 'node:child_process';
import { accessSync, constants, realpathSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const defaultMaximumParallelTests = 50;
const maximumParallelTests = 50;
const exclusiveParallelTests = 1;
const simdOptions = ['DOTNET_EnableHWIntrinsic', 'COMPlus_EnableHWIntrinsic',
  'DOTNET_EnableSIMD', 'COMPlus_EnableSIMD'];
const heavyLoadEnabledOption = 'HeavyLoad:Enabled';
const heavyLoadFilter = '/*/*/HeavyDocumentLoadClusterTests/*';
const ordinaryClusterFilter = '/*/*/*/*[Category!=HeavyLoad]';
const ordinaryUnitFilter = '/KeyLoad.UnitTests/*/*/*';
const projects = new Map([
  ['analyzers', 'KeyLoad.Analyzers.Tests'], ['unit', 'KeyLoad.UnitTests'],
  ['recovery', 'KeyLoad.RecoveryTests'],
  ['cluster', 'KeyLoad.IntegrationTests'], ['site', 'KeyLoad.SiteTests']
]);
const mapped = new Map([
  ['Filter', '--treenode-filter'], ['ResultsDirectory', '--results-directory'],
  ['CoverageSettings', '--coverage-settings'], ['CoverageOutput', '--coverage-output'],
  ['CoverageFormat', '--coverage-output-format'], ['Execution:MaximumParallelTests', '--maximum-parallel-tests']
]);
// Selection only: workloads and Aspire resource lifetimes belong to the C# TUnit tests.
export function runtimeSelection(input, inherited = process.env) {
  if (simdOptions.some(key => inherited[key] !== undefined && inherited[key] !== '' && inherited[key] !== '1')) {
    throw new Error('Execution SIMD must remain enabled.');
  }
  const values = new Map();
  for (const argument of input) {
    const match = /^--KeyLoadTests:([^=]+)=(.+)$/u.exec(argument);
    if (!match || values.has(match[1])) throw new Error('Invalid or duplicate execution test selection.');
    if (match[2].length > 4096) throw new Error('Execution test argument exceeds its bound.');
    values.set(match[1], match[2]);
  }
  const suite = values.get('Suite');
  const project = projects.get(suite);
  if (!project) throw new Error('Unsupported execution TUnit suite.');
  const heavyLoad = values.has(heavyLoadEnabledOption);
  if (!heavyLoad && values.get('Filter') === heavyLoadFilter) {
    throw new Error('Heavy CLUSTER load requires its explicit enabled selection.');
  }
  if (heavyLoad && (values.get(heavyLoadEnabledOption) !== 'true' || suite !== 'cluster'
    || values.get('Filter') !== heavyLoadFilter || values.has('CoverageSettings')
    || [...values.keys()].some(key => key.startsWith('RuntimeCoverage:')))) {
    throw new Error('Heavy CLUSTER load requires its exact exclusive functional selection.');
  }
  const parallel = Number(values.get('Execution:MaximumParallelTests')
    ?? (heavyLoad ? exclusiveParallelTests : defaultMaximumParallelTests));
  if (!Number.isInteger(parallel) || parallel < 1 || parallel > maximumParallelTests) throw new Error('Invalid execution parallelism.');
  if (heavyLoad && parallel !== exclusiveParallelTests) {
    throw new Error('Heavy load requires exactly one execution test at a time.');
  }
  if (suite === 'cluster' && !values.has('Filter')) values.set('Filter', ordinaryClusterFilter);
  if (suite === 'unit' && !values.has('Filter')) values.set('Filter', ordinaryUnitFilter);
  const environment = { ...inherited };
  if (process.platform === 'darwin') {
    // Storage roots reject symlink ancestors; MTP pipes need a short existing Unix base.
    for (const [key, fallback] of [['TMPDIR', '/private/tmp'],
      ['TESTINGPLATFORM_PIPE_DIRECTORY', '/private/tmp']]) {
      try {
        const selected = environment[key] || fallback;
        if (!path.isAbsolute(selected)) throw new Error('Relative directory.');
        const physical = realpathSync.native(selected);
        if (!statSync(physical).isDirectory()) throw new Error('Not a directory.');
        accessSync(physical, constants.R_OK | constants.W_OK | constants.X_OK);
        environment[key] = physical;
      } catch {
        throw new Error(`Invalid execution macOS ${key} directory.`);
      }
    }
  }
  for (const key of ['KeyLoadTests__Suite', 'KeyLoadTests__HeavyLoad__Enabled']) delete environment[key];
  const args = ['test', '--project', `tests/${project}`, '--no-build', '--no-restore',
    '--output', 'Detailed', '--github-reporter-style', 'full', '--maximum-parallel-tests', String(parallel),
    '--results-directory', path.resolve(root, values.get('ResultsDirectory') ?? `TestResults/${suite}`)];
  args.push('--timeout', `${values.get('TimeoutMinutes') ?? (suite === 'cluster' ? 60 : 30)}m`);
  for (const [key, value] of values) {
    if (['Suite', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key)) continue;
    if (key === 'ReportTrx') {
      if (!['true', 'false'].includes(value)) throw new Error('Invalid TRX selection.');
      if (value === 'true') args.push('--report-trx');
    } else if (key === 'TimeoutMinutes') {
      const minutes = Number(value);
      if (!Number.isInteger(minutes) || minutes < 1 || minutes > 180) throw new Error('Invalid execution timeout.');

    } else if (mapped.has(key)) {
      args.push(mapped.get(key), key.startsWith('Coverage') && key !== 'CoverageFormat' ? path.resolve(root, value) : value);
    } else if (key === heavyLoadEnabledOption) {
      environment.KeyLoadTests__HeavyLoad__Enabled = value;
    } else throw new Error('Unsupported execution test argument.');
  }
  const coverageSettings = values.has('CoverageSettings');
  if (coverageSettings !== values.has('CoverageOutput')) throw new Error('Coverage settings and output must be selected together.');
  if (coverageSettings) {
    args.push('--coverage');
    if (!values.has('CoverageFormat')) args.push('--coverage-output-format', 'cobertura');
  }
  return { args, environment };
}

export async function runTests(input) {
  const selection = runtimeSelection(input);
  const child = spawn('dotnet', selection.args, { cwd: root, env: selection.environment, stdio: 'inherit' });
  const interrupt = () => child.kill('SIGINT');
  const terminate = () => child.kill('SIGTERM');
  process.on('SIGINT', interrupt);
  process.on('SIGTERM', terminate);
  try {
    return await new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', (code, signal) => resolve(code ?? (signal === 'SIGINT' ? 130 : signal === 'SIGTERM' ? 143 : 1)));
    });
  } finally {
    process.off('SIGINT', interrupt);
    process.off('SIGTERM', terminate);
  }
}

const evaluated = process.execArgv.some(argument => argument === '--eval' || argument === '-e');
if (!evaluated && process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runTests(process.argv.slice(2)).then(code => { process.exitCode = code; }).catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
