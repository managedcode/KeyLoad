import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { fileName } from './image-contracts.mjs';
import { createRunContext, validateEntryArguments } from './image-inputs.mjs';
import { verifyDockerEngine, verifyDockerfilePins } from './image-engine.mjs';
import { verifySourceCheckout } from './prepare-images.mjs';
import { bundleDiagnostic, bundleDirectory, bundleFile, imageNames } from './image-bundle-contract.mjs';
import { createDirectory, writeExclusive } from './image-bundle-files.mjs';
import { readPreparedImages } from './image-bundle-read.mjs';
import { saveNativeImage } from './image-bundle-native.mjs';

export async function exportImages(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  await verifySourceCheckout(context);
  await verifyDockerfilePins(context.workspace);
  const prepared = await readPreparedImages(context.evidenceDirectory, context);
  await verifyDockerEngine(context);
  const directory = await createDirectory(path.join(context.runnerTemp, bundleDirectory));
  await writeExclusive(path.join(directory, fileName.receipt), prepared.receiptBytes);
  const images = {};
  for (const name of imageNames) {
    const image = prepared.images[name];
    await writeExclusive(path.join(directory, image.record.manifestFile), image.bytes);
    images[name] = await saveNativeImage(context, directory, image);
  }
  const bundle = { schemaVersion: 1, sourceRevision: context.sourceSha, runId: context.runId,
    attempt: context.runAttempt, repository: context.repository, ref: context.ref, images };
  await writeExclusive(path.join(directory, bundleFile.descriptor), Buffer.from(`${JSON.stringify(bundle, null, 2)}\n`));
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await exportImages(); } catch (error) {
    process.stderr.write(`${bundleDiagnostic(error)}\n`);
    process.exitCode = 1;
  }
}
