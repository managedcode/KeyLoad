import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { baseImage, fileName, imageKind, imageReference, message, outputFormat, processLimit, receiptField, registry, safeErrorMessage } from './image-contracts.mjs';
import { createRunContext, assertCleanSourceRevision, validateEntryArguments } from './image-inputs.mjs';
import { appendImageOutputs, createImmutableManifest, createImmutableReceipt, ensureEvidenceDirectory } from './image-evidence.mjs';
import { buildProductImage, makeImageTag, makeTaggedReference, pushProductImage, startOwnedRegistry, verifyBuildx, verifyDockerEngine, verifyDockerfilePins } from './image-engine.mjs';
import { fetchManifest, waitForRegistry } from './image-manifest.mjs';
import { runBounded } from './image-process.mjs';

const gitCommand = 'git';
const gitRevisionArguments = Object.freeze(['rev-parse', 'HEAD']);
const gitDiffArguments = Object.freeze(['diff', '--quiet', 'HEAD', '--']);
const gitStatusArguments = Object.freeze(['status', '--porcelain', '--untracked-files=no']);
const schemaVersion = 1;

export async function prepareImages(environment = process.env, argv = process.argv.slice(2)) {
  const kinds = preparationImageKinds(argv);
  const context = createRunContext(environment, process.platform);
  await ensureEvidenceDirectory(context);
  await verifySourceCheckout(context);
  await verifyDockerfilePins(context.workspace);
  await verifyDockerEngine(context);
  await verifyBuildx(context);

  const tag = makeImageTag(context);
  await startOwnedRegistry(context);
  await waitForRegistry(context);

  const images = {};
  const references = {};
  for (const kind of kinds) {
    const tagged = makeTaggedReference(kind, tag);
    const built = await buildProductImage(context, kind, tagged);
    await pushProductImage(context, tagged);
    const isServer = kind === imageKind.server;
    const name = isServer ? imageReference.serverName : imageReference.comparisonsName;
    const filename = isServer ? fileName.serverManifest : fileName.comparisonsManifest;
    const manifest = await fetchManifest(context, name, tag, built);
    await createImmutableManifest(context, filename, manifest.manifestBytes);
    const key = isServer ? receiptField.server : receiptField.comparisons;
    images[key] = makeImageRecord(manifest, filename, built);
    references[key] = manifest.finalReference;
  }
  const receipt = Object.freeze({
    [receiptField.schemaVersion]: schemaVersion,
    [receiptField.sourceRevision]: context.sourceSha,
    [receiptField.github]: Object.freeze({
      [receiptField.runId]: context.runId,
      [receiptField.attempt]: context.runAttempt,
      [receiptField.repository]: context.repository,
      [receiptField.ref]: context.ref,
    }),
    [receiptField.bases]: Object.freeze({
      [receiptField.sdk]: baseImage.sdk,
      [receiptField.runtime]: baseImage.aspnet,
      [receiptField.registry]: registry.image,
    }),
    [receiptField.images]: Object.freeze(images),
  });

  await createImmutableReceipt(context, receipt);
  await appendImageOutputs(context, references[receiptField.server], references[receiptField.comparisons]);
}

export function preparationImageKinds(argv) {
  if (Array.isArray(argv) && argv.length === 1 && argv[0] === '--server-only') {
    return Object.freeze([imageKind.server]);
  }
  validateEntryArguments(argv);
  return Object.freeze([imageKind.server, imageKind.comparisons]);
}

export function makeImageRecord(manifest, manifestFile, builtImage) {
  return Object.freeze({
    [receiptField.reference]: manifest.finalReference,
    [receiptField.manifestDigest]: manifest.manifest.manifestSha256,
    [receiptField.registryDigest]: manifest.manifest.digestHeader,
    [receiptField.manifestFile]: manifestFile,
    [receiptField.revisionLabel]: manifest.sourceLabelValue,
    [receiptField.configId]: builtImage.configImageId,
  });
}

export async function verifySourceCheckout(context) {
  const revisionResult = await runBounded(gitCommand, gitRevisionArguments, {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
  if (revisionResult.code !== 0) throw new Error(message.invalidWorkspace);
  const diffResult = await runBounded(gitCommand, gitDiffArguments, {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
  const statusResult = await runBounded(gitCommand, gitStatusArguments, {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
  if (statusResult.code !== 0) throw new Error(message.invalidWorkspace);
  assertCleanSourceRevision(context, revisionResult.stdout, diffResult.code, statusResult.stdout);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    await prepareImages();
  } catch (error) {
    process.stderr.write(`${safeErrorMessage(error, message.commandFailed)}${outputFormat.newline}`);
    process.exitCode = 1;
  }
}
