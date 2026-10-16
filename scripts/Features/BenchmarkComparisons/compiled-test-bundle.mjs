import { constants } from 'node:fs';
import { lstat, mkdir, open, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createRunContext } from './image-inputs.mjs';
import { verifySourceCheckout } from './prepare-images.mjs';
import { runtimeBundle, requireRuntime, selectRuntimeAspire, validateRuntimeDescriptor, validateRuntimeInventory } from './compiled-test-bundle-contract.mjs';
import { collectRuntimeFiles, hashRuntimeFile, readRuntimeJson, requireRuntimeDestination } from './compiled-test-bundle-files.mjs';
import { runBundleNative } from './compiled-test-bundle-native.mjs';

async function requirePlainDirectory(directory) {
  for (let current = directory; current !== path.dirname(current); current = path.dirname(current)) {
    const metadata = await lstat(current);
    requireRuntime(metadata.isDirectory() && !metadata.isSymbolicLink());
  }
}

async function requireArchive(directory, descriptor, inventory) {
  const archive = path.join(directory, runtimeBundle.archive);
  const record = await hashRuntimeFile(archive, runtimeBundle.maximumArchiveBytes);
  requireRuntime(record.sha256 === descriptor.archiveSha256 && record.bytes === descriptor.archiveBytes);
  const names = (await runBundleNative('tar', ['--list', '--gzip', '--file', archive, '--quoting-style=literal'], directory))
    .toString('utf8').trimEnd().split('\n');
  requireRuntime(names.length === inventory.length && names.every((name, index) => name === inventory[index].path));
  const types = (await runBundleNative('tar', ['--list', '--verbose', '--gzip', '--file', archive, '--quoting-style=literal'], directory))
    .toString('utf8').trimEnd().split('\n');
  requireRuntime(types.length === inventory.length && types.every(line => line.startsWith('-')));
}

export async function packCompiledRuntime(environment = process.env) {
  const context = createRunContext(environment, process.platform);
  await verifySourceCheckout(context);
  requireRuntime(environment.NUGET_PACKAGES === path.join(context.workspace, runtimeBundle.packages));
  const directory = path.join(context.workspace, 'artifacts/benchmark-runtime-bundle');
  await mkdir(directory, { mode: 0o700 });
  await requirePlainDirectory(directory);
  const tracked = await runBundleNative('git', ['ls-files', '-z', '--', '*.csproj'], context.workspace, 1048576);
  const projects = tracked.toString('utf8').split('\0').filter(Boolean);
  const configuration = await readRuntimeJson(path.join(context.workspace, 'global.json'), 65536);
  const aspireVersion = configuration['msbuild-sdks']?.['Aspire.AppHost.Sdk'];
  const inventory = await collectRuntimeFiles(context.workspace, projects, aspireVersion);
  process.stdout.write(`Compiled runtime: ${inventory.length} original files inventoried.\n`);
  const inventoryBytes = Buffer.from(JSON.stringify(inventory) + '\n');
  requireRuntime(inventoryBytes.length <= runtimeBundle.maximumInventoryBytes);
  await writeFile(path.join(directory, runtimeBundle.inventory), inventoryBytes, { flag: 'wx', mode: 0o600 });
  const list = path.join(directory, runtimeBundle.list);
  await writeFile(list, inventory.map(entry => entry.path).join('\0') + '\0', { flag: 'wx', mode: 0o600 });
  const archive = path.join(directory, runtimeBundle.archive);
  await runBundleNative('tar', ['--create', '--gzip', '--file', archive, '--hard-dereference',
    '--no-recursion', '--null', '--verbatim-files-from', '--files-from', list], context.workspace, 1048576);
  const fileRecord = await hashRuntimeFile(archive, runtimeBundle.maximumArchiveBytes);
  const inventoryRecord = await hashRuntimeFile(path.join(directory, runtimeBundle.inventory), runtimeBundle.maximumInventoryBytes);
  const files = new Map(inventory.map(entry => [entry.path, entry]));
  const descriptor = {
    schemaVersion: runtimeBundle.schemaVersion, kind: runtimeBundle.kind,
    sourceRevision: context.sourceSha, runId: Number(context.runId), attempt: Number(context.runAttempt),
    repository: context.repository, ref: context.ref, platform: 'linux', architecture: process.arch,
    workspace: context.workspace,
    sdkVersion: (await runBundleNative('dotnet', ['--version'], context.workspace, 1024)).toString('utf8').trim(),
    archive: runtimeBundle.archive, archiveSha256: fileRecord.sha256, archiveBytes: fileRecord.bytes,
    inventory: runtimeBundle.inventory, inventorySha256: inventoryRecord.sha256, fileCount: inventory.length,
    aspire: selectRuntimeAspire(inventory, aspireVersion, process.arch),
    assemblies: runtimeBundle.assemblies.map(([role, stem]) => ({
      role, path: stem + '.dll', sha256: files.get(stem + '.dll')?.sha256,
      pdbPath: stem + '.pdb', pdbSha256: files.get(stem + '.pdb')?.sha256,
    })),
  };
  validateRuntimeDescriptor(descriptor, context, inventory);
  await requireArchive(directory, descriptor, inventory);
  process.stdout.write('Compiled runtime: archive and native Aspire identities verified.\n');
  await writeFile(path.join(directory, runtimeBundle.descriptor), JSON.stringify(descriptor, null, 2) + '\n',
    { flag: 'wx', mode: 0o600 });
  return descriptor;
}

export async function importCompiledRuntime(directory, environment = process.env) {
  const context = createRunContext(environment, process.platform);
  await verifySourceCheckout(context);
  requireRuntime(path.isAbsolute(directory));
  await requirePlainDirectory(directory);
  await requirePlainDirectory(context.workspace);
  requireRuntime(environment.NUGET_PACKAGES === path.join(context.workspace, runtimeBundle.packages));
  const names = ['benchmark-runtime-files.list', runtimeBundle.archive, runtimeBundle.descriptor, runtimeBundle.inventory];
  requireRuntime((await readdir(directory)).sort().join('\0') === names.sort().join('\0'));
  const inventory = validateRuntimeInventory(await readRuntimeJson(path.join(directory, runtimeBundle.inventory), runtimeBundle.maximumInventoryBytes));
  const descriptor = validateRuntimeDescriptor(await readRuntimeJson(path.join(directory, runtimeBundle.descriptor), 65536), context, inventory);
  const configuration = await readRuntimeJson(path.join(context.workspace, 'global.json'), 65536);
  requireRuntime(descriptor.aspire.version === configuration['msbuild-sdks']?.['Aspire.AppHost.Sdk']);
  const inventoryRecord = await hashRuntimeFile(path.join(directory, runtimeBundle.inventory), runtimeBundle.maximumInventoryBytes);
  requireRuntime(inventoryRecord.sha256 === descriptor.inventorySha256);
  await requireArchive(directory, descriptor, inventory);
  process.stdout.write('Compiled runtime: source and archive verified; importing original files.\n');
  await requireRuntimeDestination(context.workspace, inventory);
  await runBundleNative('tar', ['--extract', '--gzip', '--file', path.join(directory, runtimeBundle.archive),
    '--directory', context.workspace, '--no-same-owner', '--no-same-permissions', '--keep-old-files', '--no-overwrite-dir'], context.workspace, 1048576);
  for (const entry of inventory) {
    const actual = await hashRuntimeFile(path.join(context.workspace, entry.path));
    requireRuntime(actual.bytes === entry.bytes && actual.sha256 === entry.sha256
      && (actual.mode & 0o111) === (entry.mode & 0o111));
  }
  await verifySourceCheckout(context);
  await writeRuntimeImport(directory, descriptor, context, environment);
  process.stdout.write(`Compiled runtime: ${inventory.length} original file hashes verified; native runner ready.\n`);
  return descriptor;
}

async function writeRuntimeImport(directory, descriptor, context, environment) {
  const root = environment.KEYLOAD_COMPARISON_EVIDENCE_DIRECTORY;
  const session = environment.KEYLOAD_BENCHMARK_SESSION_ID;
  requireRuntime(typeof root === 'string' && path.isAbsolute(root)
    && /^[a-z]+-n[13]$/u.test(session ?? '')
    && root === path.join(context.workspace, 'artifacts/comparisons/sessions/workers', session));
  await mkdir(root, { recursive: true, mode: 0o700 });
  await requirePlainDirectory(root);
  const record = await hashRuntimeFile(path.join(directory, runtimeBundle.descriptor), 65536);
  const receipt = {
    schemaVersion: 1, kind: 'keyload-benchmark-runtime-import', sourceRevision: descriptor.sourceRevision,
    runId: descriptor.runId, attempt: descriptor.attempt, repository: descriptor.repository,
    ref: descriptor.ref, workspace: descriptor.workspace, platform: descriptor.platform,
    architecture: descriptor.architecture, sdkVersion: descriptor.sdkVersion,
    descriptorSha256: record.sha256, inventorySha256: descriptor.inventorySha256,
    archiveSha256: descriptor.archiveSha256, fileCount: descriptor.fileCount, assemblies: descriptor.assemblies,
  };
  await writeFile(path.join(root, 'runtime-import.json'), JSON.stringify(receipt, null, 2) + '\n',
    { flag: 'wx', mode: 0o600 });
  requireRuntime(typeof environment.GITHUB_ENV === 'string' && path.isAbsolute(environment.GITHUB_ENV));
  const handle = await open(environment.GITHUB_ENV, constants.O_WRONLY | constants.O_APPEND | constants.O_NOFOLLOW);
  try {
    await handle.writeFile(`ASPIRE_DCP_PATH=${path.dirname(path.join(context.workspace, descriptor.aspire.dcpPath))}\nASPIRE_DASHBOARD_PATH=${path.join(context.workspace, descriptor.aspire.dashboardPath)}\n`);
    await handle.sync();
  } finally { await handle.close(); }
}

async function main(argv) {
  requireRuntime(argv.length === 1);
  if (argv[0] === '--pack') await packCompiledRuntime();
  else {
    requireRuntime(argv[0].startsWith('--import='));
    await importCompiledRuntime(argv[0].slice('--import='.length));
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { await main(process.argv.slice(2)); }
  catch (error) {
    const message = /^Compiled benchmark runtime rejected: [A-Za-z]+\.$/u.test(error?.message ?? '')
      ? error.message : 'Compiled benchmark runtime rejected.';
    process.stderr.write(message + '\n');
    process.exitCode = 1;
  }
}
