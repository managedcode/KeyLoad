import { assertIsolated } from './isolated-contracts.mjs';

const TOKEN = /\s*("(?:[^"\\\x00-\x1F]|\\(?:["\\\x2Fbfnrt]|u[a-fA-F0-9]{4}))*"|[{}\[\],:]|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/y;
const MAX_DEPTH = 64;

function checkDuplicateKeys(source) {
  const frames = [];
  let position = 0;
  while (position < source.length) {
    TOKEN.lastIndex = position;
    const match = TOKEN.exec(source);
    if (match === null) {
      assertIsolated(source.slice(position).trim().length === 0);
      return;
    }
    position = TOKEN.lastIndex;
    const token = match[1];
    const frame = frames.at(-1);
    if (token === '{' || token === '[') {
      frames.push(token === '{' ? { keys: new Set(), property: true } : null);
      assertIsolated(frames.length <= MAX_DEPTH);
    } else if (token === '}' || token === ']') {
      frames.pop();
    } else if (token === ',' && frame) {
      frame.property = true;
    } else if (token.startsWith('"') && frame?.property) {
      const key = JSON.parse(token);
      assertIsolated(!frame.keys.has(key));
      frame.keys.add(key);
      frame.property = false;
    }
  }
}

export function parseIsolatedJson(bytes) {
  try {
    const source = new TextDecoder('utf8', { fatal: true }).decode(bytes);
    checkDuplicateKeys(source);
    return JSON.parse(source);
  } catch {
    assertIsolated(false);
  }
}

export function confinedUrl(value, base, directory = false) {
  assertIsolated(typeof value === 'string' && !/[\\%]/.test(value) && !/(?:^|\/)\.\.?(?:\/|$)/.test(value));
  const url = new URL(value, base);
  assertIsolated((url.protocol === 'https:' || url.protocol === 'http:' && url.hostname === '127.0.0.1') &&
    !url.username && !url.password && !url.search && !url.hash && (!directory || url.pathname.endsWith('/')));
  const page = globalThis.document?.baseURI ?? globalThis.location?.href;
  if (page) assertIsolated(url.origin === new URL(page).origin);
  if (base) assertIsolated(url.origin === new URL(base).origin && url.pathname.startsWith(new URL(base).pathname));
  return url;
}

export function assertNotAborted(signal) {
  if (signal?.aborted) throw new DOMException('The isolated evidence request was cancelled.', 'AbortError');
}

async function readBytes(response, limit, signal) {
  assertIsolated(response.ok && response.body);
  const header = response.headers.get('content-length');
  const declared = Number(header);
  assertIsolated(header === null || Number.isSafeInteger(declared) && declared >= 0 && declared <= limit);
  const reader = response.body.getReader();
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      assertNotAborted(signal);
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      assertIsolated(size <= limit);
      chunks.push(value);
    }
    assertNotAborted(signal);
  } catch (error) {
    await reader.cancel().catch(() => {});
    throw error;
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return bytes;
}

export async function fetchIsolatedBytes(url, limit, signal) {
  assertNotAborted(signal);
  return readBytes(await fetch(url, { cache: 'no-store', redirect: 'error', signal }), limit, signal);
}
