import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { fail, format } from './functional-coverage.server-image-contracts.mjs';
import { getToolClosure } from './functional-coverage.server-image-tool.mjs';
import {
  appendInventory,
  assertDirectoryPath,
  copyBoundedRegularFile,
  makeParentDirectories,
  readBoundedRegularFile,
  readFileMetadata,
  walkRegularTree,
  writeCreateOnly,
} from './functional-coverage.server-image-files.mjs';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const sourceFiles = Object.freeze({
  contracts: path.join(scriptDirectory, 'functional-coverage.server-image-contracts.mjs'),
  files: path.join(scriptDirectory, 'functional-coverage.server-image-files.mjs'),
  tool: path.join(scriptDirectory, 'functional-coverage.server-image-tool.mjs'),
  materializer: fileURLToPath(import.meta.url),
  entry: path.join(scriptDirectory, 'functional-coverage.server-image.mjs'),
  dockerfile: path.join(scriptDirectory, 'functional-coverage.server-image.dockerfile'),
  dockerignore: path.join(scriptDirectory, '.dockerignore'),
  settings: path.join(scriptDirectory, 'functional-coverage.server.settings.xml'),
  wrapper: path.join(scriptDirectory, 'functional-coverage.server-wrapper.sh'),
  lifecycle: path.join(scriptDirectory, 'functional-coverage.server-lifecycle.sh'),
  target: path.join(scriptDirectory, 'functional-coverage.server-target.sh'),
});

export function renderTemplate(sourceBytes, sourceMode, placeholder, replacement, message) {
  const text = sourceBytes.toString('utf8');
  if (text.split(placeholder).length !== 2) {
    fail(message);
  }
  return {
    bytes: Buffer.from(text.replace(placeholder, replacement), 'utf8'),
    sourceSha256: crypto.createHash('sha256').update(sourceBytes).digest('hex'),
    sourceMode,
  };
}

function writeRuntimeIdentity(invocation, toolClosureDigest, settingsSha256) {
  const values = [
    ['KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256', invocation.server.dllSha256],
    ['KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256', invocation.server.pdbSha256],
    ['KEYLOAD_NATIVE_COVERAGE_SERVER_MVID', invocation.server.mvid],
    ['KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256', invocation.server.sourceReceiptSha256],
    ['KEYLOAD_NATIVE_COVERAGE_TOOL_VERSION', invocation.tool.version],
    ['KEYLOAD_NATIVE_COVERAGE_TOOL_CLOSURE_DIGEST', toolClosureDigest],
    ['KEYLOAD_NATIVE_COVERAGE_SETTINGS_SHA256', settingsSha256],
    ['KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS', String(invocation.bounds.shutdownSeconds)],
    ['KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS', String(invocation.bounds.settlementSeconds)],
    ['KEYLOAD_NATIVE_COVERAGE_MAX_REPORT_BYTES', String(invocation.bounds.maximumReportBytes)],
  ];
  return Buffer.from(`${values.map(([key, value]) => `${key}=${value}`).join('\n')}\n`, 'utf8');
}

function readIdentityInputs(invocation, bounds) {
  const serverReceipt = readBoundedRegularFile(invocation.server.sourceReceiptPath,
    bounds.maximumManifestBytes, 'The server source receipt is invalid.');
  const baseReceipt = readBoundedRegularFile(invocation.baseImage.sourceReceiptPath,
    bounds.maximumManifestBytes, 'The source-bound ASP.NET image receipt is invalid.');
  verifyReceiptDigests(serverReceipt.bytes, baseReceipt.bytes, invocation);
  return {
    serverBytes: serverReceipt.bytes,
    serverMode: serverReceipt.mode,
    baseBytes: baseReceipt.bytes,
    baseMode: baseReceipt.mode,
  };
}

function verifyReceiptDigests(serverBytes, baseBytes, invocation) {
  const serverDigest = crypto.createHash('sha256').update(serverBytes).digest('hex');
  const baseDigest = crypto.createHash('sha256').update(baseBytes).digest('hex');
  if (serverDigest !== invocation.server.sourceReceiptSha256 ||
      baseDigest !== invocation.baseImage.sourceReceiptSha256) {
    fail('A source-bound image receipt changed after admission.');
  }
}

function readPreparationTemplates(bounds) {
  const settings = readFileMetadata(sourceFiles.settings, bounds.maximumManifestBytes,
    'The server coverage settings are invalid.', bounds);
  const wrapper = readFileMetadata(sourceFiles.wrapper, bounds.maximumManifestBytes,
    'The server coverage wrapper is invalid.', bounds);
  const lifecycle = readFileMetadata(sourceFiles.lifecycle, bounds.maximumManifestBytes,
    'The server coverage lifecycle helper is invalid.', bounds);
  const target = readFileMetadata(sourceFiles.target, bounds.maximumManifestBytes,
    'The original Server target wrapper is invalid.', bounds);
  const dockerfile = readBoundedRegularFile(sourceFiles.dockerfile, bounds.maximumManifestBytes,
    'The server Dockerfile template is invalid.');
  const dockerignore = readBoundedRegularFile(sourceFiles.dockerignore, bounds.maximumManifestBytes,
    'The server Docker ignore file is invalid.');
  dockerignore.sha256 = crypto.createHash('sha256').update(dockerignore.bytes).digest('hex');
  return { settings, wrapper, lifecycle, target, dockerfile, dockerignore };
}

function readProducerSources(bounds) {
  const sources = {};
  for (const [name, sourcePath] of Object.entries(sourceFiles)) {
    if (['dockerfile', 'dockerignore', 'settings', 'wrapper', 'lifecycle', 'target'].includes(name)) {
      continue;
    }
    sources[name] = readFileMetadata(sourcePath, bounds.maximumManifestBytes,
      'A native coverage materializer source is invalid.', bounds);
  }
  return sources;
}

function createServerInventory(invocation, bounds, estimate) {
  const entries = walkRegularTree(invocation.serverPublishDirectory, format.serverDirectoryName, bounds, estimate);
  for (const entry of entries) {
    checkOutputPath(invocation.contextDirectory, entry.relativePath, bounds);
  }
  const dll = entries.find((entry) => entry.relativePath === `${format.serverDirectoryName}/${format.serverDllName}`);
  const pdb = entries.find((entry) => entry.relativePath === `${format.serverDirectoryName}/${format.serverPdbName}`);
  if (!dll || !pdb) {
    fail('The admitted server publish closure is missing its original DLL or PDB.');
  }
  if (dll.sha256 !== invocation.server.dllSha256 || pdb.sha256 !== invocation.server.pdbSha256) {
    fail('The published Server binaries differ from native identity admission.');
  }
  return entries;
}

function checkOutputPath(contextDirectory, relativePath, bounds) {
  const targetPath = path.join(contextDirectory, ...relativePath.split('/'));
  if (targetPath.length > bounds.maximumPathCharacters) {
    fail('A materialized image path exceeds the admitted path bound.');
  }
}

function addToolInventory(invocation, bounds, estimate) {
  const closure = getToolClosure(invocation.tool.packageRoot, invocation.tool.version, bounds);
  const entries = [...closure.packageFiles, ...closure.licenseFiles];
  for (const entry of entries) {
    if (entry.sourcePath.length > bounds.maximumPathCharacters) {
      fail('A native tool source path exceeds the admitted path bound.');
    }
    checkOutputPath(invocation.contextDirectory, entry.relativePath, bounds);
    estimate.fileCount += 1;
    estimate.totalBytes += BigInt(entry.length);
  }
  return { closure, entries };
}

function calculateToolDigest(packageEntries) {
  const inventory = packageEntries.map(({ relativePath, mode, length, sha256 }) => ({
    path: relativePath,
    mode,
    length,
    sha256,
  }));
  inventory.sort((left, right) => left.path.localeCompare(right.path, 'en'));
  return crypto.createHash('sha256').update(JSON.stringify(inventory), 'utf8').digest('hex');
}

function renderGeneratedFiles(invocation, bounds, templates, receipts, toolDigest) {
  const settingsRead = readBoundedRegularFile(sourceFiles.settings, bounds.maximumManifestBytes,
    'The server coverage settings changed.');
  const wrapperRead = readBoundedRegularFile(sourceFiles.wrapper, bounds.maximumManifestBytes,
    'The server coverage wrapper changed.');
  const lifecycleRead = readBoundedRegularFile(sourceFiles.lifecycle, bounds.maximumManifestBytes,
    'The server coverage lifecycle helper changed.');
  const targetRead = readBoundedRegularFile(sourceFiles.target, bounds.maximumManifestBytes,
    'The original Server target wrapper changed.');
  const dockerignoreRead = readBoundedRegularFile(sourceFiles.dockerignore, bounds.maximumManifestBytes,
    'The server Docker ignore file changed.');
  const dockerfile = renderTemplate(templates.dockerfile.bytes, templates.dockerfile.mode,
    '{{SOURCE_BOUND_ASPNET_IMAGE}}', invocation.baseImage.reference,
    'The server Dockerfile template is invalid.');
  const runtimeIdentity = writeRuntimeIdentity(invocation, toolDigest, templates.settings.sha256);
  return [
    generatedFile(format.settingsName, settingsRead.bytes, templates.settings.mode),
    generatedFile(format.wrapperName, wrapperRead.bytes, templates.wrapper.mode | 0o111, templates.wrapper.mode),
    generatedFile(format.lifecycleName, lifecycleRead.bytes, templates.lifecycle.mode),
    generatedFile(format.targetName, targetRead.bytes, templates.target.mode | 0o111, templates.target.mode),
    generatedFile(format.dockerfileName, dockerfile.bytes, dockerfile.sourceMode),
    generatedFile(format.dockerignoreName, dockerignoreRead.bytes, templates.dockerignore.mode),
    generatedFile(format.sourceReceiptName, receipts.serverBytes, receipts.serverMode),
    generatedFile(format.baseReceiptName, receipts.baseBytes, receipts.baseMode),
    generatedFile(format.outputRuntimeIdentityPath, runtimeIdentity, 0o644),
  ];
}

function generatedFile(relativePath, bytes, mode, sourceMode = mode) {
  return { relativePath, bytes, mode, sourceMode };
}

function verifyGeneratedTemplates(templates, renderedFiles) {
  const expected = new Map([
    [format.settingsName, templates.settings],
    [format.wrapperName, templates.wrapper],
    [format.lifecycleName, templates.lifecycle],
    [format.targetName, templates.target],
    [format.dockerignoreName, templates.dockerignore],
  ]);
  for (const file of renderedFiles) {
    const original = expected.get(file.relativePath);
    if (original && (file.bytes.length !== original.length ||
        crypto.createHash('sha256').update(file.bytes).digest('hex') !== original.sha256 ||
        file.sourceMode !== original.mode)) {
      fail('A server image wrapper input changed during preparation.');
    }
  }
}

function checkGeneratedBounds(files, invocation, estimate) {
  for (const file of files) {
    checkOutputPath(invocation.contextDirectory, file.relativePath, invocation.bounds);
    if (file.bytes.length > invocation.bounds.maximumFileBytes) {
      fail('Generated image metadata exceeds the admitted file bound.');
    }
    estimate.fileCount += 1;
    estimate.totalBytes += BigInt(file.bytes.length);
  }
  if (estimate.fileCount + 1 > invocation.bounds.maximumFiles ||
      estimate.totalBytes > BigInt(invocation.bounds.maximumTotalBytes)) {
    fail('The complete image input inventory exceeds the admitted bounds.');
  }
}

function createMaterializationPlan(invocation) {
  const bounds = invocation.bounds;
  const estimate = { fileCount: 0, totalBytes: 0n };
  const templates = readPreparationTemplates(bounds);
  const receipts = readIdentityInputs(invocation, bounds);
  const producerSources = readProducerSources(bounds);
  const serverEntries = createServerInventory(invocation, bounds, estimate);
  const tool = addToolInventory(invocation, bounds, estimate);
  const toolDigest = calculateToolDigest(tool.closure.packageFiles);
  const renderedFiles = renderGeneratedFiles(invocation, bounds, templates, receipts, toolDigest);
  verifyGeneratedTemplates(templates, renderedFiles);
  checkGeneratedBounds(renderedFiles, invocation, estimate);
  return { bounds, estimate, templates, receipts, producerSources, serverEntries, tool, toolDigest, renderedFiles };
}

function writeCopiedFiles(contextDirectory, entries, bounds, aggregate, inventory) {
  for (const entry of entries) {
    const destination = path.join(contextDirectory, ...entry.relativePath.split('/'));
    makeParentDirectories(contextDirectory, entry.relativePath);
    const copied = copyBoundedRegularFile(entry.sourcePath, destination, bounds, aggregate);
    if ((entry.size !== undefined && copied.length !== entry.size) ||
        (entry.length !== undefined && copied.length !== entry.length) || copied.mode !== entry.mode ||
        copied.sha256 !== entry.sha256) {
      fail('An admitted image input changed during context materialization.');
    }
    appendInventory(inventory, entry.relativePath, copied);
  }
}

function writeGeneratedFiles(contextDirectory, files, bounds, aggregate, inventory) {
  for (const file of files) {
    const destination = path.join(contextDirectory, ...file.relativePath.split('/'));
    makeParentDirectories(contextDirectory, file.relativePath);
    const metadata = writeCreateOnly(destination, file.bytes, file.mode, bounds, aggregate);
    appendInventory(inventory, file.relativePath, metadata);
  }
}

function writeSourceReceiptFiles(contextDirectory, plan, bounds, aggregate, inventory) {
  const files = [
    { relativePath: format.sourceReceiptName, bytes: plan.receipts.serverBytes, mode: plan.receipts.serverMode },
    { relativePath: format.baseReceiptName, bytes: plan.receipts.baseBytes, mode: plan.receipts.baseMode },
  ];
  writeGeneratedFiles(contextDirectory, files, bounds, aggregate, inventory);
}

function createManifest(invocation, plan, inventory) {
  inventory.sort((left, right) => left.path.localeCompare(right.path, 'en'));
  return {
    schemaVersion: format.schemaVersion,
    invocationId: invocation.invocationId,
    producerSources: plan.producerSources,
    sourceTemplates: {
      dockerfileSha256: crypto.createHash('sha256').update(plan.templates.dockerfile.bytes).digest('hex'),
      dockerfileMode: plan.templates.dockerfile.mode,
      dockerignoreSha256: crypto.createHash('sha256').update(plan.templates.dockerignore.bytes).digest('hex'),
      dockerignoreMode: plan.templates.dockerignore.mode,
      settingsSha256: plan.templates.settings.sha256,
      wrapperSha256: plan.templates.wrapper.sha256,
      wrapperMode: plan.templates.wrapper.mode,
      lifecycleSha256: plan.templates.lifecycle.sha256,
      lifecycleMode: plan.templates.lifecycle.mode,
      targetSha256: plan.templates.target.sha256,
      targetMode: plan.templates.target.mode,
    },
    baseImage: {
      reference: invocation.baseImage.reference,
      sourceReceiptSha256: invocation.baseImage.sourceReceiptSha256,
    },
    server: {
      dllName: format.serverDllName,
      dllSha256: invocation.server.dllSha256,
      pdbName: format.serverPdbName,
      pdbSha256: invocation.server.pdbSha256,
      mvid: invocation.server.mvid,
      sourceReceiptSha256: invocation.server.sourceReceiptSha256,
    },
    tool: {
      packageId: plan.tool.closure.packageMetadata.packageId,
      version: plan.tool.closure.packageMetadata.version,
      repositoryCommit: plan.tool.closure.packageMetadata.repositoryCommit,
      nupkgSha256: plan.tool.closure.packageArchive.sha256,
      nupkgSha512: plan.tool.closure.packageArchive.sha512,
      closureDigest: plan.toolDigest,
    },
    bounds: { ...plan.bounds },
    files: inventory,
  };
}

function writeContextManifest(contextDirectory, invocation, plan, inventory, aggregate) {
  checkOutputPath(contextDirectory, format.outputManifestPath, plan.bounds);
  const bytes = Buffer.from(`${JSON.stringify(createManifest(invocation, plan, inventory))}\n`, 'utf8');
  if (bytes.length > plan.bounds.maximumManifestBytes) {
    fail('The image context manifest exceeds its admitted manifest bound.');
  }
  const metadata = writeCreateOnly(path.join(contextDirectory, format.outputManifestPath), bytes,
    0o644, plan.bounds, aggregate);
  return { metadata, bytes };
}

function copyPlanInputs(contextDirectory, plan, aggregate, inventory) {
  const bounds = plan.bounds;
  writeCopiedFiles(contextDirectory, plan.serverEntries, bounds, aggregate, inventory);
  writeCopiedFiles(contextDirectory, plan.tool.entries, bounds, aggregate, inventory);
  writeGeneratedFiles(contextDirectory, plan.renderedFiles.filter((file) =>
    file.relativePath !== format.sourceReceiptName && file.relativePath !== format.baseReceiptName),
  bounds, aggregate, inventory);
  writeSourceReceiptFiles(contextDirectory, plan, bounds, aggregate, inventory);
}

function prepareContextDirectory(invocation, bounds) {
  assertDirectoryPath(invocation.contextDirectory, true, bounds.maximumPathCharacters);
  if (fs.existsSync(invocation.contextDirectory)) {
    fail('The image context path already exists.');
  }
  fs.mkdirSync(invocation.contextDirectory, { mode: 0o700 });
  return fs.lstatSync(invocation.contextDirectory, { bigint: true });
}

function removeOwnedContextDirectory(contextDirectory, identity) {
  let current;
  try {
    current = fs.lstatSync(contextDirectory, { bigint: true });
  } catch {
    return;
  }
  if (!current.isDirectory() || current.isSymbolicLink() || current.dev !== identity.dev || current.ino !== identity.ino) {
    fail('The image context ownership changed before cleanup.');
  }
  fs.rmSync(contextDirectory, { recursive: true, force: false });
}

export async function materialize(invocation) {
  const plan = createMaterializationPlan(invocation);
  const contextIdentity = prepareContextDirectory(invocation, plan.bounds);
  let completed = false;
  let primaryError;
  const aggregate = { fileCount: 0, totalBytes: 0n };
  try {
    const inventory = [];
    copyPlanInputs(invocation.contextDirectory, plan, aggregate, inventory);
    const written = writeContextManifest(invocation.contextDirectory, invocation, plan, inventory, aggregate);
    completed = true;
    return {
      contextDirectory: invocation.contextDirectory,
      manifestPath: path.join(invocation.contextDirectory, format.outputManifestPath),
      manifestSha256: crypto.createHash('sha256').update(written.bytes).digest('hex'),
      fileCount: aggregate.fileCount,
      totalBytes: Number(aggregate.totalBytes),
    };
  } catch (error) {
    primaryError = error;
    throw error;
  } finally {
    if (!completed) {
      try {
        removeOwnedContextDirectory(invocation.contextDirectory, contextIdentity);
      } catch (cleanupError) {
        if (primaryError) {
          throw new AggregateError([primaryError, cleanupError], 'Native coverage context creation and cleanup failed.');
        }
        throw cleanupError;
      }
    }
  }
}
