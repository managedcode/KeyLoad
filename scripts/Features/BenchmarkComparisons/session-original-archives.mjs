import path from 'node:path';
import { mkdtemp, rm } from 'node:fs/promises';
import { hashRegularFile } from './isolated-github-files.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { inspectZip, extractEntry } from './isolated-github-zip.mjs';
import { SESSION, requireSession } from './session-contract.mjs';

// Every extracted input must still match the original API-digest-bound ZIP at aggregation time.
export async function verifySessionOriginalArchive(input, archive, artifact, references, maximum) {
  await validateDownloadedArchive(archive, {digest:artifact.digest,size_in_bytes:artifact.sizeInBytes}, maximum);
  const temporary = await mkdtemp(path.join(input, '.session-original-'));
  try {
    const context = {execution:{workspace:input}};
    await inspectZip(archive, references.map(file => file.entry), path.join(temporary,'inventory.txt'), context);
    for (const [index, file] of references.entries()) {
      const actual = await hashRegularFile(path.join(input,file.path), file.maximum ?? SESSION.reportBytes);
      requireSession(actual.sha256 === file.sha256);
      const output = path.join(temporary,String(index));
      const original = await extractEntry(archive,file.entry,output,file.maximum ?? SESSION.reportBytes,context);
      requireSession(original.sha256 === actual.sha256 && original.bytes === actual.bytes);
      await rm(output);
    }
  } finally { await rm(temporary,{recursive:true,force:true}); }
}
