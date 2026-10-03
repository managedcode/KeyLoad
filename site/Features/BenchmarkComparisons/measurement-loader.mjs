const HASH = Object.freeze({ algorithm: 'SHA-256', radix: 16, padding: 2, leadingZero: '0' });

export function sha256(bytes) {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  return globalThis.crypto.subtle.digest(HASH.algorithm, view).then(digest =>
    [...new Uint8Array(digest)].map(byte => byte.toString(HASH.radix)
      .padStart(HASH.padding, HASH.leadingZero)).join(''));
}
