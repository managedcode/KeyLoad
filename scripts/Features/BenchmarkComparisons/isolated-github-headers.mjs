import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { writeCapture } from './isolated-github-files.mjs';

export function parseResponseHeaders(bytes) {
  requireGitHub(Buffer.isBuffer(bytes) && bytes.length > 0 && bytes.length <= GH.headerBytes);
  const lines = bytes.toString('latin1').trimEnd().split(/\r?\n/);
  const status = /^HTTP\/\d(?:\.\d)? ([1-5]\d\d)(?: [^\x00-\x1f\x7f]*)?$/.exec(lines.shift());
  requireGitHub(status !== null);
  const headers = {};
  for (const line of lines) {
    const match = /^([A-Za-z0-9-]+):[ \t]*([^\x00-\x08\x0a-\x1f\x7f]*)$/.exec(line);
    requireGitHub(match !== null && !Object.hasOwn(headers, match[1].toLowerCase()));
    headers[match[1].toLowerCase()] = match[2].trim();
  }
  return { status: Number(status[1]), headers };
}

function headerEnd(bytes) {
  for (const marker of [Buffer.from('\r\n\r\n'), Buffer.from('\n\r\n'), Buffer.from('\n\n')]) {
    const index = bytes.indexOf(marker);
    if (index >= 0) return index + marker.length;
  }
  return -1;
}

export function responseDecoder(target) {
  let pending = Buffer.alloc(0);
  let response;
  return {
    get response() { return response; },
    async decode(chunk) {
      if (response) return chunk;
      pending = Buffer.concat([pending, chunk]);
      const end = headerEnd(pending);
      if (end < 0) { requireGitHub(pending.length <= GH.headerBytes); return Buffer.alloc(0); }
      requireGitHub(end <= GH.headerBytes);
      const bytes = pending.subarray(0, end);
      response = parseResponseHeaders(bytes);
      await writeCapture(target, bytes);
      const body = pending.subarray(end);
      pending = Buffer.alloc(0);
      return body;
    },
  };
}
