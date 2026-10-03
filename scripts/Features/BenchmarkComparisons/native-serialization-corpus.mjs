import { readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { NATIVE, exactKeys, positiveInteger, requireNative } from './native-serialization-contract.mjs';
import { collectFacts, hashObject, readBounded, requireDirectory } from './native-serialization-files.mjs';

/** Validates setup-only corpus receipts, never claiming they authenticate benchmark execution. */
export function validateCorpusManifest(value, expectedFixture, expectedSize) {
  requireNative(exactKeys(value, ['fixture', 'payloadBytes', 'corpusSha256', 'nativeSha256', 'nativeBytes', 'jsonSha256', 'jsonBytes']), 'corpus.schema');
  requireNative(NATIVE.types.includes(expectedFixture) && NATIVE.sizes.includes(expectedSize)
    && value.fixture === expectedFixture && value.payloadBytes === expectedSize, 'corpus.identity');
  requireNative(NATIVE.digest.test(value.corpusSha256 ?? '') && NATIVE.digest.test(value.nativeSha256 ?? '')
    && NATIVE.digest.test(value.jsonSha256 ?? '') && value.corpusSha256 === value.jsonSha256, 'corpus.hash');
  requireNative(positiveInteger(value.nativeBytes) && positiveInteger(value.jsonBytes)
    && value.nativeBytes >= expectedSize && value.jsonBytes >= expectedSize
    && value.nativeBytes <= NATIVE.reportBytes && value.jsonBytes <= NATIVE.reportBytes, 'corpus.bytes');
}

export async function captureCorpus(directory) {
  const root = join(directory, 'corpus');
  await requireDirectory(root);
  const files = (await readdir(root, { withFileTypes: true }));
  const expected = NATIVE.types.flatMap(type => NATIVE.sizes.map(size => `${type}-${size}.json`));
  requireNative(files.length === 6 && files.every(file => file.isFile() && expected.includes(file.name)), 'corpus.complete');
  const manifests = [];
  for (const type of NATIVE.types) {
    for (const size of NATIVE.sizes) {
      const name = `${type}-${size}.json`;
      const value = JSON.parse((await readBounded(join(root, name), 16384)).toString('utf8'));
      validateCorpusManifest(value, type, size);
      manifests.push(value);
    }
  }
  const facts = await collectFacts(root, expected);
  requireNative(new Set(manifests.map(value => value.corpusSha256)).size === 6
    && new Set(manifests.map(value => value.nativeSha256)).size === 6, 'corpus.distinct');
  return { manifests, files: facts, sha256: hashObject(facts) };
}
