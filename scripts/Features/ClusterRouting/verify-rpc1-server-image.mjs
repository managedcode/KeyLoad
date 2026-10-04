import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { outputFormat, safeErrorMessage } from '../BenchmarkComparisons/image-contracts.mjs';
import { createRunContext, validateEntryArguments } from '../BenchmarkComparisons/image-inputs.mjs';
import { createRpc1ExpectedProducer, verifyRpc1ServerProof } from './rpc1-server-proof.mjs';

const receiptName = 'rpc1-epoch6-server-image-receipt.json';
const manifestName = 'rpc1-epoch6-server-manifest.json';
const receiptEnvironmentName = 'KEYLOAD_RPC1_IMAGE_RECEIPT';
const manifestEnvironmentName = 'KEYLOAD_RPC1_SERVER_MANIFEST';
const referenceEnvironmentName = 'KEYLOAD_RPC1_SERVER_IMAGE';
const invalidInput = 'The immutable RPC1 server image proof is invalid.';

export async function verifyRpc1ServerImage(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  const producer = createRpc1ExpectedProducer(environment, context);
  const receiptPath = environment[receiptEnvironmentName];
  const manifestPath = environment[manifestEnvironmentName];
  const expectedReceipt = path.join(context.evidenceDirectory, receiptName);
  const expectedManifest = path.join(context.evidenceDirectory, manifestName);
  const expectedReference = environment[referenceEnvironmentName];
  if (receiptPath !== expectedReceipt || manifestPath !== expectedManifest || typeof expectedReference !== 'string'
    || expectedReference.trim() !== expectedReference || /[\u0000-\u001f\u007f]/.test(expectedReference)) throw new Error(invalidInput);
  const proof = await verifyRpc1ServerProof(receiptPath, producer, expectedReference);
  return proof.reference;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(`${await verifyRpc1ServerImage()}${outputFormat.newline}`); }
  catch (error) {
    process.stderr.write(`${safeErrorMessage(error, invalidInput)}${outputFormat.newline}`);
    process.exitCode = 1;
  }
}
