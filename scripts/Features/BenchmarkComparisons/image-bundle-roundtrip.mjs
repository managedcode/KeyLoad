import { rename } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { envName, imageReference } from './image-contracts.mjs';
import { createRunContext, validateEntryArguments } from './image-inputs.mjs';
import { ensureEvidenceDirectory } from './image-evidence.mjs';
import { cleanupImages } from './cleanup-images.mjs';
import { exportImages } from './export-images.mjs';
import { importImages } from './import-images.mjs';
import { bundleDiagnostic, bundleDirectory, bundleFile, imageNames, requireBundle, retainedDirectory } from './image-bundle-contract.mjs';
import { readRegular, requireAbsent, writeExclusive } from './image-bundle-files.mjs';
import { readImageBundle, readPreparedImages } from './image-bundle-read.mjs';
import { removeRoundTripOwnedImages } from './image-bundle-removal.mjs';
import { verifyNativeImage } from './image-bundle-native.mjs';

const importOutputName = 'image-bundle-import-output';
const proofName = 'image-bundle-roundtrip.json';

export async function roundTripImages(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  const prepared = await readPreparedImages(context.evidenceDirectory, context);
  const retained = path.join(context.runnerTemp, retainedDirectory);
  const bundlePath = path.join(context.runnerTemp, bundleDirectory);
  const outputPath = path.join(context.runnerTemp, importOutputName);
  await requireAbsent(retained);
  await requireAbsent(outputPath);
  let cleanupAttempted = false;
  let moved = false;
  let proof;
  try {
    await exportImages(environment, []);
    await readImageBundle(bundlePath, context);
    cleanupAttempted = true;
    await cleanupImages(environment, []);
    await rename(context.evidenceDirectory, retained);
    moved = true;
    await ensureEvidenceDirectory(context);
    await removeRoundTripOwnedImages(context, prepared);
    await writeExclusive(outputPath, Buffer.alloc(0));
    await importImages({ ...environment, [envName.githubOutput]: outputPath }, [`--bundle=${bundlePath}`]);
    proof = await verifyRoundTrip(context, prepared, outputPath);
  } finally {
    if (moved || !cleanupAttempted) await cleanupImages(environment, []);
  }
  const completed = Object.freeze({ ...proof, cleanupCompleted: true });
  await writeExclusive(path.join(context.evidenceDirectory, proofName), Buffer.from(`${JSON.stringify(completed, null, 2)}\n`));
  return completed;
}

async function verifyRoundTrip(context, prepared, outputPath) {
  const imported = await readPreparedImages(context.evidenceDirectory, context);
  const buildBytes = await readRegular(path.join(context.evidenceDirectory, bundleFile.buildReceipt));
  requireBundle(buildBytes.equals(prepared.receiptBytes) && imported.receiptBytes.equals(prepared.receiptBytes));
  for (const name of imageNames) {
    requireBundle(imported.images[name].bytes.equals(prepared.images[name].bytes));
    await verifyNativeImage(context, imported.images[name]);
  }
  const server = imported.receipt.images.server.reference;
  const runner = imported.receipt.images.comparisons.reference;
  const expectedOutputs = `${imageReference.outputServer}=${server}\n${imageReference.outputLoadGenerator}=${runner}\n`;
  requireBundle((await readRegular(outputPath)).equals(Buffer.from(expectedOutputs)));
  return Object.freeze({ sourceRevision: context.sourceSha, archives: imageNames.length,
    nativeIdsAbsentBeforeImport: true, nativeIdsExact: true, manifestBytesExact: true,
    buildReceiptBytesExact: true, importReceiptBytesExact: true, outputsExact: true,
    serverImage: server, runnerImage: runner });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(`${JSON.stringify(await roundTripImages())}\n`); } catch (error) {
    process.stderr.write(`${bundleDiagnostic(error)}\n`);
    process.exitCode = 1;
  }
}
