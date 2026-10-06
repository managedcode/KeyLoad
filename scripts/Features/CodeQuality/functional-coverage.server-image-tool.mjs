import crypto from 'node:crypto';
import path from 'node:path';
import { fail, format } from './functional-coverage.server-image-contracts.mjs';
import {
  assertDirectoryPath,
  hashBoundedRegularFile,
  readBoundedRegularFile,
  readFileMetadata,
} from './functional-coverage.server-image-files.mjs';

export function parseNuspec(nuspecBytes, expectedVersion) {
  const text = nuspecBytes.toString('utf8');
  const packageId = /<id>\s*([^<]+?)\s*<\/id>/i.exec(text)?.[1];
  const version = /<version>\s*([^<]+?)\s*<\/version>/i.exec(text)?.[1];
  const repositoryCommit = /<repository\b[^>]*\bcommit="([0-9a-f]{40})"/i.exec(text)?.[1];
  if (packageId !== format.packageId || version !== expectedVersion || !repositoryCommit) {
    fail('The cached native coverage package metadata is invalid.');
  }
  return { packageId, version, repositoryCommit };
}

export function safeRelativeAsset(value) {
  if (typeof value !== 'string' || value.length === 0 || path.posix.isAbsolute(value) ||
      value.includes('\\') || value.split('/').some((part) => part === '' || part === '.' || part === '..')) {
    fail('The native tool dependency manifest contains an unsafe asset path.');
  }
  return value;
}

function verifyPackageArchives(packageRoot, version, bounds) {
  const archive = path.join(packageRoot, `${format.packageId}.${version}.nupkg`);
  const sidecarPath = `${archive}.sha512`;
  const archiveSha256 = hashBoundedRegularFile(archive, bounds.maximumFileBytes,
    'The native package archive is invalid.', bounds);
  const archiveSha512 = hashBoundedRegularFile(archive, bounds.maximumFileBytes,
    'The native package archive is invalid.', bounds, 'sha512');
  const sidecar = readBoundedRegularFile(sidecarPath, bounds.maximumManifestBytes,
    'The native package archive receipt is invalid.');
  const expectedSha512 = sidecar.bytes.toString('utf8').trim();
  if (Buffer.from(archiveSha512.digest, 'hex').toString('base64') !== expectedSha512) {
    fail('The native package archive differs from its cached package receipt.');
  }
  return { sha256: archiveSha256.digest, sha512: expectedSha512 };
}

function addAsset(assets, value, bounds) {
  assets.add(safeRelativeAsset(value));
  if (assets.size > bounds.maximumFiles) {
    fail('The native tool dependency inventory exceeds the admitted file bound.');
  }
}

function readDependencyAssets(toolRoot, version, bounds) {
  const depsPath = path.join(toolRoot, format.dependencyManifestName);
  const depsRead = readBoundedRegularFile(depsPath, bounds.maximumManifestBytes,
    'The native tool dependency manifest is invalid.');
  let deps;
  try {
    deps = JSON.parse(depsRead.bytes.toString('utf8'));
  } catch {
    fail('The native tool dependency manifest is invalid.');
  }
  const target = deps.targets?.['.NETCoreApp,Version=v8.0'];
  if (!target || !Object.hasOwn(target, `${format.packageId}/${version}`)) {
    fail('The native tool dependency manifest does not match its package version.');
  }
  const assets = new Set([format.dependencyManifestName, format.runtimeConfigName]);
  for (const library of Object.values(target)) {
    appendLibraryAssets(assets, library, bounds);
  }
  for (const relativePath of format.linuxNativeFiles) {
    addAsset(assets, path.posix.join(format.linuxNativeDirectory, relativePath), bounds);
  }
  return [...assets].sort((left, right) => left.localeCompare(right, 'en'));
}

function appendLibraryAssets(assets, library, bounds) {
  for (const [assetPath, asset] of Object.entries(library.runtime ?? {})) {
    const localPath = typeof asset.localPath === 'string' ? asset.localPath : path.posix.basename(assetPath);
    addAsset(assets, localPath, bounds);
  }
  for (const [assetPath, asset] of Object.entries(library.resources ?? {})) {
    const localPath = typeof asset.localPath === 'string'
      ? asset.localPath
      : path.posix.join(asset.locale ?? '', path.posix.basename(assetPath));
    addAsset(assets, localPath, bounds);
  }
}

function readPackageFiles(toolRoot, assetPaths, bounds) {
  const entries = [];
  for (const relativePath of assetPaths) {
    const sourcePath = path.join(toolRoot, ...relativePath.split('/'));
    const metadata = readFileMetadata(sourcePath, bounds.maximumFileBytes,
      'A native coverage tool file is missing or invalid.', bounds);
    entries.push({ sourcePath, relativePath: `tool/${relativePath}`, ...metadata });
  }
  return entries;
}

function readLicenseFiles(packageRoot, bounds) {
  const entries = [];
  for (const fileName of format.licenseFiles) {
    const sourcePath = path.join(packageRoot, fileName);
    const metadata = readFileMetadata(sourcePath, bounds.maximumFileBytes,
      'A native coverage license file is missing or invalid.', bounds);
    entries.push({ sourcePath, relativePath: `license/${fileName}`, ...metadata });
  }
  return entries;
}

export function getToolClosure(packageRoot, version, bounds) {
  packageRoot = assertDirectoryPath(packageRoot, false, bounds.maximumPathCharacters);
  if (path.basename(packageRoot) !== version) {
    fail('The cached native coverage package version does not match its pinned identity.');
  }
  const toolRoot = path.join(packageRoot, format.packageToolDirectory);
  const canonicalToolRoot = assertDirectoryPath(toolRoot, false, bounds.maximumPathCharacters);
  const nuspec = path.join(packageRoot, `${format.packageId}.nuspec`);
  const nuspecRead = readBoundedRegularFile(nuspec, bounds.maximumManifestBytes,
    'The native package manifest is invalid.');
  const packageMetadata = parseNuspec(nuspecRead.bytes, version);
  const packageArchive = verifyPackageArchives(packageRoot, version, bounds);
  const assetPaths = readDependencyAssets(canonicalToolRoot, version, bounds);
  return {
    packageMetadata,
    packageArchive,
    packageFiles: readPackageFiles(canonicalToolRoot, assetPaths, bounds),
    licenseFiles: readLicenseFiles(packageRoot, bounds),
  };
}
