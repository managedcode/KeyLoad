import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { outputFormat, safeErrorMessage } from '../BenchmarkComparisons/image-contracts.mjs';
import { createRunContext, validateEntryArguments } from '../BenchmarkComparisons/image-inputs.mjs';
import { createExpectedProducer } from './prepare-native5-server-image.mjs';
import { verifyNative5ServerProof } from './native5-server-proof.mjs';

const receiptName = 'prior-server-image-receipt.json';
const manifestName = 'prior-server-manifest.json';
const receiptEnvironmentName = 'KEYLOAD_PRIOR_IMAGE_RECEIPT';
const manifestEnvironmentName = 'KEYLOAD_PRIOR_SERVER_MANIFEST';
const referenceEnvironmentName = 'KEYLOAD_PRIOR_SERVER_IMAGE';
const invalidInput = 'The immutable native5 server image proof is invalid.';

export async function verifyNative5ServerImage(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  const producer = createExpectedProducer(environment, context);
  const receiptPath = environment[receiptEnvironmentName];
  const manifestPath = environment[manifestEnvironmentName];
  const expectedReceipt = path.join(context.evidenceDirectory, receiptName);
  const expectedManifest = path.join(context.evidenceDirectory, manifestName);
  const expectedReference = environment[referenceEnvironmentName];
  if (receiptPath !== expectedReceipt || manifestPath !== expectedManifest || typeof expectedReference !== 'string'
    || expectedReference.trim() !== expectedReference || /[\u0000-\u001f\u007f]/.test(expectedReference)) {
    throw new Error(invalidInput);
  }
  const proof = await verifyNative5ServerProof(receiptPath, producer, expectedReference);
  return proof.reference;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const reference = await verifyNative5ServerImage();
    process.stdout.write(`${reference}${outputFormat.newline}`);
  } catch (error) {
    process.stderr.write(`${safeErrorMessage(error, invalidInput)}${outputFormat.newline}`);
    process.exitCode = 1;
  }
}
