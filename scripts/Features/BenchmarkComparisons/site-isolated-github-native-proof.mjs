import { mkdtemp, rm } from 'node:fs/promises';
import path from 'node:path';
import { streamToFile } from './isolated-github-stream.mjs';
import { GH } from './isolated-github-contract.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';

export async function verifyOriginalSiteEntries(input, receipt) {
  const stage = await mkdtemp(path.join(input, '.site-isolated-verify-'));
  const entries = [
    ['suite', 'aggregate.json', 'input/aggregate/aggregate.json'],
    ['provider', 'proof.json', 'input/provider/proof.json'],
    ['provider', 'image-proof.json', 'input/provider/image-proof.json'],
  ];
  try {
    for (const [name, entry, relative] of entries) {
      const expected = receipt.inputFiles.find(file => file.path === relative);
      const archive = path.join(input, receipt.archives[name].path);
      const actual = await streamToFile('unzip', ['-p', archive, entry], path.join(stage, name + '-' + entry),
        SITE_GH.jsonBytes, GH.unzipTimeoutMs, input);
      requireSite(expected !== undefined && actual.bytes === expected.bytes && actual.sha256 === expected.sha256);
    }
  } finally { await rm(stage, { recursive: true, force: true }); }
}
