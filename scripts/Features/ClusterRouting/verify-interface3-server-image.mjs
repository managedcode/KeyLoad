import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { outputFormat, safeErrorMessage } from '../BenchmarkComparisons/image-contracts.mjs';
import { createRunContext, validateEntryArguments } from '../BenchmarkComparisons/image-inputs.mjs';
import { createInterface3ExpectedProducer, verifyInterface3ServerProof } from './interface3-server-proof.mjs';
import { interface3ServerSource } from './interface3-server-source.mjs';

const receiptName = 'interface3-epoch7-server-image-receipt.json';
const manifestName = 'interface3-epoch7-server-manifest.json';
const receiptEnvironmentName = 'KEYLOAD_INTERFACE3_IMAGE_RECEIPT';
const manifestEnvironmentName = 'KEYLOAD_INTERFACE3_SERVER_MANIFEST';
const inventoryEnvironmentName = 'KEYLOAD_INTERFACE3_SERVER_INVENTORY';
const archiveEnvironmentName = 'KEYLOAD_INTERFACE3_SERVER_ARCHIVE';
const referenceEnvironmentName = 'KEYLOAD_INTERFACE3_SERVER_IMAGE';
const invalidInput = 'The immutable interface3 server image proof is invalid.';

export async function verifyInterface3ServerImage(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  const producer = createInterface3ExpectedProducer(environment, context);
  const evidence = context.evidenceDirectory;
  const receiptPath = environment[receiptEnvironmentName];
  const manifestPath = environment[manifestEnvironmentName];
  const inventoryPath = environment[inventoryEnvironmentName];
  const archivePath = environment[archiveEnvironmentName];
  const expectedReference = environment[referenceEnvironmentName];
  if (receiptPath !== path.join(evidence, receiptName)
    || manifestPath !== path.join(evidence, manifestName)
    || inventoryPath !== path.join(evidence, interface3ServerSource.inventoryName)
    || archivePath !== path.join(evidence, interface3ServerSource.archiveName)
    || typeof expectedReference !== 'string' || expectedReference.trim() !== expectedReference
    || /[\u0000-\u001f\u007f]/.test(expectedReference)) throw new Error(invalidInput);
  const proof = await verifyInterface3ServerProof(receiptPath, producer, expectedReference, context);
  return proof.reference;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { process.stdout.write(`${await verifyInterface3ServerImage()}${outputFormat.newline}`); }
  catch (error) {
    process.stderr.write(`${safeErrorMessage(error, invalidInput)}${outputFormat.newline}`);
    process.exitCode = 1;
  }
}
