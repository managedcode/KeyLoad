import { homedir } from 'node:os';
import { join } from 'node:path';
import { NATIVE, requireNative } from './native-serialization-contract.mjs';
import { collectFacts, fileFact, hashObject, readBounded } from './native-serialization-files.mjs';
import { command, sourceFacts } from './native-serialization-process.mjs';

const packageVersions = Object.freeze({ BenchmarkDotNet: '0.15.8',
  'Microsoft.Orleans.Serialization': '10.4.0', 'Microsoft.Orleans.CodeGenerator': '10.4.0' });
const assemblies = ['KeyLoad.Benchmarks.dll', 'KeyLoad.BenchmarkScenarios.dll', 'KeyLoad.Abstractions.dll',
  'BenchmarkDotNet.dll', 'Orleans.Serialization.dll'];
const environmentKeys = ['DOTNET_EnableHWIntrinsic', 'DOTNET_TieredPGO', 'DOTNET_TieredCompilation',
  'DOTNET_GCServer', 'DOTNET_gcServer', 'COMPlus_EnableHWIntrinsic', 'COMPlus_TieredPGO',
  'COMPlus_TieredCompilation', 'COMPlus_gcServer', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'RUNNER_ARCH', 'RUNNER_OS'];

export async function packageFacts(environment) {
  const central = (await readBounded('Directory.Packages.props')).toString('utf8');
  const root = environment.NUGET_PACKAGES || join(homedir(), '.nuget', 'packages');
  const facts = [];
  for (const [name, version] of Object.entries(packageVersions)) {
    const escaped = name.replaceAll('.', '\\.');
    requireNative(new RegExp(`Include="${escaped}"\\s+Version="${version.replaceAll('.', '\\.')}"`).test(central), 'packages.pin');
    const id = name.toLowerCase();
    const file = await fileFact(join(root, id, version, `${id}.${version}.nupkg`), `${name}/${version}`);
    facts.push({ name, version, bytes: file.bytes, sha256: file.sha256 });
  }
  return facts;
}

async function runtimeFacts() {
  const output = await command('dotnet', ['--list-runtimes']);
  const selected = output.split('\n').map(line => /^Microsoft\.NETCore\.App (10\.\d+\.\d+) \[(.+)\]$/.exec(line.trim()))
    .filter(Boolean);
  requireNative(selected.length > 0, 'runtime.installed');
  const runtimeAssemblies = [];
  for (const match of selected) {
    const fact = await fileFact(join(match[2], match[1], 'System.Text.Json.dll'), `Microsoft.NETCore.App/${match[1]}/System.Text.Json.dll`);
    runtimeAssemblies.push(fact);
  }
  return { installedRuntimes: output, jsonAssemblies: runtimeAssemblies };
}

export async function captureExecution(environment) {
  const sdkConfiguration = JSON.parse((await readBounded('global.json')).toString('utf8'));
  requireNative(sdkConfiguration.sdk?.version === NATIVE.sdk
    && (await command('dotnet', ['--version'])).trim() === NATIVE.sdk, 'sdk.pin');
  const sources = await sourceFacts(environment);
  const builtAssemblies = await collectFacts(process.cwd(), assemblies.map(name => `${NATIVE.bin}/${name}`));
  const packages = await packageFacts(environment);
  const dotnetInfo = await command('dotnet', ['--info']);
  const runtime = await runtimeFacts();
  const variables = Object.fromEntries(environmentKeys.filter(key => environment[key] !== undefined)
    .map(key => [key, environment[key]]));
  return { sources, assemblies: builtAssemblies, packages, environment: { dotnetInfo, runtime, variables },
    environmentSha256: hashObject({ dotnetInfo, runtime, variables }) };
}

export function requireSameExecution(before, after) {
  requireNative(before.sources.sha256 === after.sources.sha256
    && JSON.stringify(before.assemblies) === JSON.stringify(after.assemblies)
    && JSON.stringify(before.packages) === JSON.stringify(after.packages)
    && before.environmentSha256 === after.environmentSha256, 'execution.changed');
}
