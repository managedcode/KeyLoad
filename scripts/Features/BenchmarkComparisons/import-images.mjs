import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createRunContext } from './image-inputs.mjs';
import { verifyDockerEngine, verifyDockerfilePins } from './image-engine.mjs';
import { verifySourceCheckout } from './prepare-images.mjs';
import { bundleDiagnostic, imageNames, requireBundle } from './image-bundle-contract.mjs';
import { parseImportArguments } from './image-bundle-cli.mjs';
import { readImageBundle } from './image-bundle-read.mjs';
import { loadNativeImage } from './image-bundle-native.mjs';
import { prepareImportOutputs, publishImportedImages } from './image-bundle-publish.mjs';

export async function importImages(environment = process.env, argv = process.argv.slice(2)) {
  const directory = parseImportArguments(argv);
  const context = createRunContext(environment, process.platform);
  const prepared = await readImageBundle(directory, context);
  await verifySourceCheckout(context);
  await verifyDockerfilePins(context.workspace);
  await prepareImportOutputs(context, prepared);
  await verifyDockerEngine(context);
  const loaded = {};
  for (const name of imageNames) loaded[name] = await loadNativeImage(context, prepared, name);
  // A second bounded read prevents unnoticed input mutation during native loading.
  const checked = await readImageBundle(directory, context);
  requireBundle(checked.receiptBytes.equals(prepared.receiptBytes)
    && JSON.stringify(checked.bundle) === JSON.stringify(prepared.bundle));
  for (const name of imageNames) {
    requireBundle(checked.images[name].bytes.equals(prepared.images[name].bytes));
  }
  await publishImportedImages(context, prepared, loaded);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await importImages(); } catch (error) {
    process.stderr.write(`${bundleDiagnostic(error)}\n`);
    process.exitCode = 1;
  }
}
