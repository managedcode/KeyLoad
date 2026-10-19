import { readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { EXECUTION, exactKeys, positiveInteger, runtimeRequire } from './serialization-contract.mjs';
import { collectFacts, hashObject, readBounded, requireDirectory } from './serialization-files.mjs';

/** Validates setup-only corpus receipts, never claiming they authenticate benchmark execution. */
export function validateCorpusManifest(value, expectedFixture, expectedSize) {
  runtimeRequire(exactKeys(value, ['fixture', 'payloadBytes', 'corpusSha256', 'runtimeSha256', 'runtimeBytesCore', 'jsonSha256', 'jsonBytes']), 'corpus.schema');
  runtimeRequire(EXECUTION.types.includes(expectedFixture) && EXECUTION.sizes.includes(expectedSize)
    && value.fixture === expectedFixture && value.payloadBytes === expectedSize, 'corpus.identity');
  runtimeRequire(EXECUTION.digest.test(value.corpusSha256 ?? '') && EXECUTION.digest.test(value.runtimeSha256 ?? '')
    && EXECUTION.digest.test(value.jsonSha256 ?? '') && value.corpusSha256 === value.jsonSha256, 'corpus.hash');
  runtimeRequire(positiveInteger(value.runtimeBytesCore) && positiveInteger(value.jsonBytes)
    && value.runtimeBytesCore >= expectedSize && value.jsonBytes >= expectedSize
    && value.runtimeBytesCore <= EXECUTION.reportBytes && value.jsonBytes <= EXECUTION.reportBytes, 'corpus.bytes');
}

export async function captureCorpus(directory) {
  const root = join(directory, 'corpus');
  await requireDirectory(root);
  const files = (await readdir(root, { withFileTypes: true }));
  const expected = EXECUTION.types.flatMap(type => EXECUTION.sizes.map(size => `${type}-${size}.json`));
  runtimeRequire(files.length === 6 && files.every(file => file.isFile() && expected.includes(file.name)), 'corpus.complete');
  const manifests = [];
  for (const type of EXECUTION.types) {
    for (const size of EXECUTION.sizes) {
      const name = `${type}-${size}.json`;
      const value = JSON.parse((await readBounded(join(root, name), 16384)).toString('utf8'));
      validateCorpusManifest(value, type, size);
      manifests.push(value);
    }
  }
  const facts = await collectFacts(root, expected);
  runtimeRequire(new Set(manifests.map(value => value.corpusSha256)).size === 6
    && new Set(manifests.map(value => value.runtimeSha256)).size === 6, 'corpus.distinct');
  return { manifests, files: facts, sha256: hashObject(facts) };
}
