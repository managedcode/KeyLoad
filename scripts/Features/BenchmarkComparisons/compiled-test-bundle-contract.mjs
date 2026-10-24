import path from 'node:path';

export const runtimeBundle = Object.freeze({
  descriptor: 'benchmark-runtime.json', inventory: 'benchmark-runtime-files.json',
  archive: 'benchmark-runtime.tar.gz', list: 'benchmark-runtime-files.list',
  packages: 'artifacts/benchmark-runtime/packages',
  kind: 'keyload-benchmark-runtime', schemaVersion: 1,
  maximumFiles: 150000, maximumInventoryBytes: 67108864,
  maximumFileBytes: 2147483648, maximumTotalBytes: 8589934592,
  maximumArchiveBytes: 6442450944, maximumOutputBytes: 67108864,
  timeoutMs: 1800000, sha256: /^[a-f0-9]{64}$/,
  assemblies: Object.freeze([
    ['apphost', 'build/KeyLoad.AppHost/bin/Release/net10.0/KeyLoad.AppHost'],
    ['load-apphost', 'build/KeyLoad.LoadTests.AppHost/bin/Release/net10.0/KeyLoad.LoadTests.AppHost'],
    ['comparison-client', 'benchmarks/KeyLoad.ComparisonHost/bin/Release/net10.0/KeyLoad.ComparisonHost'],
    ['comparisons', 'benchmarks/KeyLoad.Comparisons/bin/Release/net10.0/KeyLoad.Comparisons'],
  ]),
});

export function requireRuntime(condition, reason = 'AdmissionFailed') {
  if (!condition) throw new Error(`Compiled benchmark runtime rejected: ${reason}.`);
}

export function allowedRuntimePath(value) {
  if (typeof value !== 'string' || value.length === 0 || value.length > 4096
    || /[\u0000-\u001f\u007f\\]/u.test(value) || path.posix.normalize(value) !== value
    || value.split('/').some(part => part === '' || part === '.' || part === '..')) return false;
  return value.startsWith(runtimeBundle.packages + '/')
    || /^(?:src|tests|benchmarks|build|analyzers)\/[A-Za-z0-9_.-]+\/(?:bin\/Release|obj)\//u.test(value);
}

export function validateRuntimeInventory(value) {
  requireRuntime(Array.isArray(value) && value.length > 0 && value.length <= runtimeBundle.maximumFiles);
  const paths = new Set();
  let bytes = 0;
  let previous = '';
  for (const entry of value) {
    requireRuntime(entry !== null && typeof entry === 'object'
      && Object.keys(entry).sort().join(',') === 'bytes,mode,path,sha256'
      && allowedRuntimePath(entry.path) && !paths.has(entry.path) && entry.path > previous
      && Number.isSafeInteger(entry.bytes) && entry.bytes >= 0 && entry.bytes <= runtimeBundle.maximumFileBytes
      && Number.isInteger(entry.mode) && entry.mode >= 0 && entry.mode <= 0o777
      && runtimeBundle.sha256.test(entry.sha256 ?? ''));
    paths.add(entry.path);
    previous = entry.path;
    bytes += entry.bytes;
    requireRuntime(Number.isSafeInteger(bytes) && bytes <= runtimeBundle.maximumTotalBytes);
  }
  return value;
}

export function validateRuntimeDescriptor(value, context, inventory) {
  requireRuntime(value?.schemaVersion === runtimeBundle.schemaVersion && value.kind === runtimeBundle.kind
    && value.sourceRevision === context.sourceSha && value.runId === Number(context.runId)
    && value.attempt === Number(context.runAttempt) && value.repository === context.repository
    && value.ref === context.ref && value.platform === 'linux' && value.architecture === process.arch
    && value.workspace === context.workspace && /^\d+\.\d+\.\d+$/u.test(value.sdkVersion ?? '')
    && value.archive === runtimeBundle.archive && value.inventory === runtimeBundle.inventory
    && runtimeBundle.sha256.test(value.archiveSha256 ?? '') && runtimeBundle.sha256.test(value.inventorySha256 ?? '')
    && Number.isSafeInteger(value.archiveBytes) && value.archiveBytes > 0
    && value.archiveBytes <= runtimeBundle.maximumArchiveBytes && value.fileCount === inventory.length
    && Array.isArray(value.assemblies) && value.assemblies.length === runtimeBundle.assemblies.length);
  const files = new Map(inventory.map(entry => [entry.path, entry]));
  for (const [index, [role, stem]] of runtimeBundle.assemblies.entries()) {
    const assembly = value.assemblies[index];
    requireRuntime(assembly?.role === role && assembly.path === stem + '.dll' && assembly.pdbPath === stem + '.pdb'
      && assembly.sha256 === files.get(assembly.path)?.sha256
      && assembly.pdbSha256 === files.get(assembly.pdbPath)?.sha256
      && runtimeBundle.sha256.test(assembly.sha256 ?? '') && runtimeBundle.sha256.test(assembly.pdbSha256 ?? ''));
  }
  const execution = selectRuntimeAspire(inventory, value.aspire?.version, value.architecture);
  requireRuntime(value.aspire?.dcpPath === execution.dcpPath && value.aspire?.dashboardPath === execution.dashboardPath);
  return value;
}

export function selectRuntimeAspire(inventory, version, architecture) {
  requireRuntime(/^\d+\.\d+\.\d+$/u.test(version) && ['x64', 'arm64'].includes(architecture));
  const prefix = `${runtimeBundle.packages}/aspire.cli.linux-${architecture}/${version}/tools/net10.0/linux-${architecture}/versions/`;
  const candidates = inventory.filter(entry => entry.path.startsWith(prefix)
    && entry.path.slice(prefix.length).startsWith(version + '_')
    && entry.path.slice(prefix.length).split('/').length === 3 && entry.path.endsWith('/dcp/dcp'));
  requireRuntime(candidates.length === 1 && candidates[0].bytes > 0 && (candidates[0].mode & 0o111) !== 0,
    'AspireBundleMissingOrAmbiguous');
  const dcpPath = candidates[0].path;
  const dashboardPath = path.posix.dirname(path.posix.dirname(dcpPath)) + '/dashboard/Aspire.Dashboard';
  const dashboard = inventory.find(entry => entry.path === dashboardPath);
  requireRuntime(dashboard !== undefined && dashboard.bytes > 0 && (dashboard.mode & 0o111) !== 0,
    'AspireDashboardMissing');
  return { version, dcpPath, dashboardPath };
}
