import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { runtimeBundle, validateRuntimeInventory, validateRuntimeDescriptor } from './compiled-test-bundle-contract.mjs';
import { SESSION, exactSession as exact, requireSession as require, sessionHash } from './session-contract.mjs';
import { readJson, hashRegularFile } from './isolated-github-files.mjs';
import { downloadArtifact } from './isolated-github-api.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { inspectNativeZip, extractNativeEntry } from './isolated-github-zip.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { projectArtifact } from './isolated-github-validation.mjs';

export const RUNTIME_IDENTITY = Object.freeze({ artifact: 'comparison-compiled-runtime-identity',
  archive: 'archives/comparison-compiled-runtime-identity.zip', maximumBytes: 134_217_728,
  step: 'Save compiled runtime identities', descriptorBytes: 65_536, receiptPath: 'runtime-import.json' });
export async function readSessionRuntimeIdentity(directory, cohort) {
  const descriptorPath = path.join(directory, runtimeBundle.descriptor);
  const inventoryPath = path.join(directory, runtimeBundle.inventory);
  const descriptor = await readJson(descriptorPath, RUNTIME_IDENTITY.descriptorBytes);
  const inventory = validateRuntimeInventory(await readJson(inventoryPath, runtimeBundle.maximumInventoryBytes));
  require(exact(descriptor, ['schemaVersion', 'kind', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'platform',
    'architecture', 'workspace', 'sdkVersion', 'archive', 'archiveSha256', 'archiveBytes', 'inventory', 'inventorySha256', 'fileCount', 'assemblies', 'aspire'])
    && typeof descriptor.workspace === 'string' && path.isAbsolute(descriptor.workspace));
  validateRuntimeDescriptor(descriptor, { sourceSha: cohort.sourceRevision, runId: String(cohort.runId),
    runAttempt: String(cohort.attempt), repository: cohort.repository, ref: cohort.ref, workspace: descriptor.workspace }, inventory);
  const descriptorRecord = await hashRegularFile(descriptorPath, RUNTIME_IDENTITY.descriptorBytes);
  const inventoryRecord = await hashRegularFile(inventoryPath, runtimeBundle.maximumInventoryBytes);
  require(inventoryRecord.sha256 === descriptor.inventorySha256);
  return { descriptor, descriptorSha256: descriptorRecord.sha256, inventorySha256: inventoryRecord.sha256 };
}
export async function collectSessionRuntimeIdentity(input, artifact, context) {
  const archive = path.join(input, RUNTIME_IDENTITY.archive);
  await downloadArtifact(artifact, archive, RUNTIME_IDENTITY.maximumBytes, context);
  const archiveRecord = await validateDownloadedArchive(archive, artifact, RUNTIME_IDENTITY.maximumBytes);
  const required = [runtimeBundle.descriptor, runtimeBundle.inventory];
  const entries = await inspectNativeZip(archive, required, path.join(input, 'github', 'runtime-identity-inventory.txt'), context);
  require(entries.length === required.length && entries.every(entry => required.includes(entry)));
  const directory = await createDirectory(path.join(input, 'github', 'runtime'));
  const files = [];
  for (const name of required) {
    const raw = await extractNativeEntry(archive, name, path.join(directory, name),
      name === runtimeBundle.descriptor ? RUNTIME_IDENTITY.descriptorBytes : runtimeBundle.maximumInventoryBytes, context);
    files.push({ path: 'github/runtime/' + name, bytes: raw.bytes, sha256: raw.sha256 });
  }
  const identity = await readSessionRuntimeIdentity(directory, context.cohort);
  return { artifact: projectArtifact(artifact), archive: { path: RUNTIME_IDENTITY.archive, ...archiveRecord }, files,
    descriptorSha256: identity.descriptorSha256, inventorySha256: identity.inventorySha256 };
}
export function validateSessionRuntimeImport(value, cohort, expected = null) {
  require(exact(value, ['schemaVersion', 'kind', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workspace',
    'platform', 'architecture', 'sdkVersion', 'descriptorSha256', 'inventorySha256', 'archiveSha256', 'fileCount', 'assemblies'])
    && value.schemaVersion === 1 && value.kind === 'keyload-benchmark-runtime-import' && value.platform === 'linux'
    && sessionHash(value.descriptorSha256) && sessionHash(value.inventorySha256) && sessionHash(value.archiveSha256)
    && typeof value.workspace === 'string' && path.isAbsolute(value.workspace)
    && ['x64', 'arm64'].includes(value.architecture) && /^\d+\.\d+\.\d+$/.test(value.sdkVersion ?? '')
    && Number.isSafeInteger(value.fileCount) && value.fileCount > 0 && value.fileCount <= runtimeBundle.maximumFiles && Array.isArray(value.assemblies)
    && value.assemblies.length === 2 && new Set(value.assemblies.map(item => item.role)).size === 2
    && value.assemblies.every(item => exact(item, ['role', 'path', 'sha256', 'pdbPath', 'pdbSha256'])
      && ['outer-tests', 'apphost'].includes(item.role) && typeof item.path === 'string'
      && item.path === runtimeBundle.assemblies.find(([role]) => role === item.role)?.[1] + '.dll'
      && item.pdbPath === runtimeBundle.assemblies.find(([role]) => role === item.role)?.[1] + '.pdb'
      && sessionHash(item.sha256) && sessionHash(item.pdbSha256)));
  for (const key of ['sourceRevision', 'runId', 'attempt', 'repository', 'ref']) require(value[key] === cohort[key]);
  if (expected !== null) {
    for (const key of ['workspace', 'platform', 'architecture', 'sdkVersion', 'archiveSha256', 'fileCount', 'assemblies'])
      require(isDeepStrictEqual(value[key], expected.descriptor[key]));
    require(value.descriptorSha256 === expected.descriptorSha256 && value.inventorySha256 === expected.inventorySha256);
  }
  return value;
}
