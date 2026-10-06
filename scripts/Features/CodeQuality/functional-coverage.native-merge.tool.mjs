import crypto from 'node:crypto';
import path from 'node:path';
import { getToolClosure } from './functional-coverage.server-image-tool.mjs';

const failure = 'The pinned native coverage tool closure does not match the retained node context.';

function canonicalInventory(entries) {
  return entries.map(({ path: itemPath, mode, length, sha256 }) => ({ path: itemPath, mode, length, sha256 }))
    .sort((left, right) => left.path.localeCompare(right.path, 'en'));
}

function digestInventory(entries) {
  return crypto.createHash('sha256').update(JSON.stringify(canonicalInventory(entries)), 'utf8').digest('hex');
}

function validateArguments(args) {
  if (args.length !== 4) { throw new Error(failure); }
  const [root, version, boundsJson, contextJson] = args;
  const bounds = JSON.parse(boundsJson);
  const contextEntries = JSON.parse(contextJson);
  const positive = [bounds.maximumFiles, bounds.maximumTotalBytes, bounds.maximumFileBytes,
    bounds.maximumPathCharacters, bounds.maximumManifestBytes, bounds.readBufferBytes];
  if (!path.isAbsolute(root) || path.basename(root) !== version || version !== '18.11.2' ||
      positive.some((value) => !Number.isSafeInteger(value) || value <= 0) ||
      contextEntries !== null && (!Array.isArray(contextEntries) || contextEntries.length > bounds.maximumFiles)) {
    throw new Error(failure);
  }
  return { root, version, bounds, contextEntries };
}

function verifyClosure({ root, version, bounds, contextEntries }) {
  const closure = getToolClosure(root, version, bounds);
  const packageEntries = closure.packageFiles;
  const licenseEntries = closure.licenseFiles;
  if (packageEntries.length + licenseEntries.length > bounds.maximumFiles) { throw new Error(failure); }
  const totalBytes = [...packageEntries, ...licenseEntries].reduce((total, entry) => total + entry.length, 0);
  if (!Number.isSafeInteger(totalBytes) || totalBytes > bounds.maximumTotalBytes) { throw new Error(failure); }
  const expectedTool = packageEntries.map(({ relativePath, mode, length, sha256 }) => ({
    path: relativePath, mode, length, sha256,
  }));
  const expectedLicense = licenseEntries.map(({ relativePath, mode, length, sha256 }) => ({
    path: relativePath, mode, length, sha256,
  }));
  if (contextEntries !== null) {
    for (const entry of contextEntries) {
      if (entry === null || Object.keys(entry).sort().join('\n') !== 'length\nmode\npath\nsha256' ||
          !Number.isSafeInteger(entry.length) || entry.length <= 0 || !Number.isInteger(entry.mode) ||
          typeof entry.path !== 'string' || !/^[0-9a-f]{64}$/.test(entry.sha256)) { throw new Error(failure); }
    }
    const actualTool = contextEntries.filter((entry) => entry.path.startsWith('tool/'));
    const actualLicense = contextEntries.filter((entry) => entry.path.startsWith('license/'));
    if (digestInventory(actualTool) !== digestInventory(expectedTool) ||
        digestInventory(actualLicense) !== digestInventory(expectedLicense) ||
        actualTool.length !== expectedTool.length || actualLicense.length !== expectedLicense.length) {
      throw new Error(failure);
    }
  }
  const toolInventory = canonicalInventory(expectedTool);
  const closureDigest = crypto.createHash('sha256').update(JSON.stringify(toolInventory), 'utf8').digest('hex');
  return {
    version,
    nupkgSha256: closure.packageArchive.sha256,
    nupkgSha512: closure.packageArchive.sha512,
    closureDigest,
    packageFileCount: packageEntries.length,
    licenseFileCount: licenseEntries.length,
  };
}

try {
  process.stdout.write(`${JSON.stringify(verifyClosure(validateArguments(process.argv.slice(2))))}\n`);
} catch {
  process.stderr.write(`${failure}\n`);
  process.exitCode = 1;
}
