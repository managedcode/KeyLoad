import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const defaultMaximumParallelTests = 20;
const maximumParallelTests = 50;
const comparisonParallelTests = 1;
const heavyLoadEnabledOption = 'HeavyLoad:Enabled';
const heavyLoadFilter = '/*/*/HeavyDocumentLoadRf3Tests/*';
const ordinaryRf3Filter = '/*/*/*/*[Category!=HeavyLoad]';
const projects = new Map([
  ['analyzers', 'KeyLoad.Analyzers.Tests'], ['unit', 'KeyLoad.UnitTests'],
  ['unit-scalar', 'KeyLoad.UnitTests'], ['recovery', 'KeyLoad.RecoveryTests'],
  ['rf3', 'KeyLoad.IntegrationTests'], ['comparison', 'KeyLoad.ComparisonTests'], ['site', 'KeyLoad.SiteTests']
]);
const localImageArgumentsEnvironment = 'KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS';
const localImageFilters = new Set(['/*/*/TwoRf3MembershipProfileTests/*',
  '/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*',
  '/*/*/RelationalSqlRf3JoinRejectionTests/*']);
const localImageGithubIdentity = ['KEYLOAD_IMAGE_RECEIPT', 'GITHUB_SHA', 'GITHUB_ACTIONS'];
const localImageInheritedIdentity = ['KEYLOAD_IMAGE_PROVENANCE', 'KEYLOAD_LOCAL_IMAGE_RECEIPT',
  'KeyLoad__ContainerImages__Server', 'KEYLOAD_LOCAL_RF3_IMAGE_CHILD',
  'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS', localImageArgumentsEnvironment];
const mapped = new Map([
  ['Filter', '--treenode-filter'], ['ResultsDirectory', '--results-directory'],
  ['CoverageSettings', '--coverage-settings'], ['CoverageOutput', '--coverage-output'],
  ['CoverageFormat', '--coverage-output-format'], ['Execution:MaximumParallelTests', '--maximum-parallel-tests']
]);

// Selection only: workloads and Aspire resource lifetimes belong to the C# TUnit tests.
export function nativeSelection(input, inherited = process.env) {
  const values = new Map();
  const workload = [];
  for (const argument of input) {
    if (argument.startsWith('--Benchmarks:')) { workload.push(argument); continue; }
    const match = /^--KeyLoadTests:([^=]+)=(.+)$/u.exec(argument);
    if (!match || values.has(match[1])) throw new Error('Invalid or duplicate native test selection.');
    if (match[2].length > 4096) throw new Error('Native test argument exceeds its bound.');
    values.set(match[1], match[2]);
  }
  const suite = values.get('Suite');
  const project = projects.get(suite);
  if (!project) throw new Error('Unsupported native TUnit suite.');
  const heavyLoad = values.has(heavyLoadEnabledOption);
  if (heavyLoad && (values.get(heavyLoadEnabledOption) !== 'true' || suite !== 'rf3'
    || values.get('Filter') !== heavyLoadFilter || values.has('CoverageSettings')
    || [...values.keys()].some(key => key.startsWith('NativeCoverage:')))) {
    throw new Error('Heavy RF3 load requires its exact exclusive functional selection.');
  }
  const parallel = Number(values.get('Execution:MaximumParallelTests')
    ?? (suite === 'comparison' || heavyLoad ? comparisonParallelTests : defaultMaximumParallelTests));
  if (!Number.isInteger(parallel) || parallel < 1 || parallel > maximumParallelTests) throw new Error('Invalid native parallelism.');
  if ((suite === 'comparison' || heavyLoad) && parallel !== comparisonParallelTests) {
    throw new Error('Comparison measurements and heavy load require exactly one native test at a time.');
  }
  const localImageEnabled = values.get('LocalRf3Image:Enabled');
  const localImageSelected = values.has('LocalRf3Image:Enabled');
  if (localImageSelected && (localImageEnabled !== 'true' || suite !== 'rf3'
    || !localImageFilters.has(values.get('Filter'))
    || [...values.keys()].some(key => !['Suite', 'Filter', 'LocalRf3Image:Enabled',
      'ReportTrx', 'TimeoutMinutes', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key))
    || localImageGithubIdentity.some(key => inherited[key] !== undefined && inherited[key] !== '')
    || localImageInheritedIdentity.some(key => inherited[key] !== undefined && inherited[key] !== ''))) {
    throw new Error('Invalid native local RF3 image selection.');
  }
  if (suite === 'rf3' && !values.has('Filter')) values.set('Filter', ordinaryRf3Filter);
  const environment = { ...inherited };
  for (const key of ['KeyLoadTests__Suite', 'KeyLoadTests__ScaleProfile', 'KeyLoadTests__VectorProfile',
    'KeyLoadTests__OpenLoopRate', 'KeyLoadTests__LocalRf3Image__Enabled', 'KeyLoadTests__HeavyLoad__Enabled',
    'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS',
    localImageArgumentsEnvironment]) delete environment[key];
  if (localImageSelected) {
    environment[localImageArgumentsEnvironment] = JSON.stringify([
      '--KeyLoadTests:Suite=' + suite, '--KeyLoadTests:Filter=' + values.get('Filter'),
      '--KeyLoadTests:LocalRf3Image:Enabled=true'
    ]);
  }
  const args = ['test', '--project', `tests/${project}`, '--no-build', '--no-restore', '--configuration', 'Release',
    '--output', 'Detailed', '--github-reporter-style', 'full', '--maximum-parallel-tests', String(parallel),
    '--results-directory', path.resolve(root, values.get('ResultsDirectory') ?? `TestResults/${suite}`)];
  if (suite === 'unit-scalar') environment.DOTNET_EnableHWIntrinsic = '0';
  args.push('--timeout', `${values.get('TimeoutMinutes') ?? (['rf3', 'comparison'].includes(suite) ? 60 : 30)}m`);
  const preparation = [];
  for (const [key, value] of values) {
    if (['Suite', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key)) continue;
    if (key === 'ReportTrx') {
      if (!['true', 'false'].includes(value)) throw new Error('Invalid TRX selection.');
      if (value === 'true') args.push('--report-trx');
    } else if (key === 'TimeoutMinutes') {
      const minutes = Number(value);
      if (!Number.isInteger(minutes) || minutes < 1 || minutes > 180) throw new Error('Invalid native timeout.');

    } else if (mapped.has(key)) {
      args.push(mapped.get(key), key.startsWith('Coverage') && key !== 'CoverageFormat' ? path.resolve(root, value) : value);
    } else if (['ScaleProfile', 'VectorProfile', 'OpenLoopRate'].includes(key)) {
      if (suite !== 'comparison') throw new Error('Benchmark selection requires ComparisonTests.');
      environment[key === 'OpenLoopRate' ? 'Benchmarks__OpenLoopRate' : `Benchmarks__${key}`] = value;
    } else if (key === heavyLoadEnabledOption) {
      environment.KeyLoadTests__HeavyLoad__Enabled = value;
    } else if (key === 'LocalRf3Image:Enabled') {
      continue;
    } else if (key.startsWith('NativeCoverage:')) {
      if (suite !== 'rf3') throw new Error('Native RF3 preparation requires IntegrationTests.');
      preparation.push(`--KeyLoadTests:${key}=${value}`);
    } else throw new Error('Unsupported native test argument.');
  }
  const coverageSettings = values.has('CoverageSettings');
  if (coverageSettings !== values.has('CoverageOutput')) throw new Error('Coverage settings and output must be selected together.');
  if (coverageSettings) {
    args.push('--coverage');
    if (!values.has('CoverageFormat')) args.push('--coverage-output-format', 'cobertura');
  }
  if (preparation.length) {
    for (const key of ['Suite', 'Filter', 'TimeoutMinutes', 'ResultsDirectory', 'CoverageSettings', 'CoverageOutput', 'CoverageFormat', 'ReportTrx']) {
      if (values.has(key)) preparation.push(`--KeyLoadTests:${key}=${values.get(key)}`);
    }
    environment.KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS = JSON.stringify(preparation);
  }
  for (const argument of workload) {
    if (suite !== 'comparison') throw new Error('Workload settings require ComparisonTests.');
    const match = /^--Benchmarks:([^=]+)=(.+)$/u.exec(argument);
    if (!match) throw new Error('Invalid workload setting.');
    environment[`Benchmarks__${match[1].replaceAll(':', '__')}`] = match[2];
  }
  return { args, environment };
}

export async function runTests(input) {
  const selection = nativeSelection(input);
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
