import { TextDecoder } from 'node:util';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';

const QUOTE = 0x22;
const COMMA = 0x2c;
const OPEN_ARRAY = 0x5b;
const CLOSE_ARRAY = 0x5d;
const OPEN_OBJECT = 0x7b;
const CLOSE_OBJECT = 0x7d;
const MAX_DEPTH = 64;
// Sample objects carry a handful of keys; a linear scan beats allocating a hash set for each of them.
const LINEAR_KEYS = 16;

function stringEnd(source, start) {
  let end = source.indexOf('"', start + 1);
  while (end !== -1) {
    let previous = end - 1;
    while (previous > start && source[previous] === '\\') previous--;
    if ((end - previous) % 2 === 1) return end;
    end = source.indexOf('"', end + 1);
  }
  requireValue(false, AGGREGATE.errors.input);
}

function keyAt(source, start, end) {
  const literal = source.slice(start + 1, end);
  return literal.includes('\\') ? JSON.parse(source.slice(start, end + 1)) : literal;
}

function open(frame, object) {
  frame.object = object;
  frame.property = object;
  frame.keys.length = 0;
  frame.set = null;
}

function addKey(frame, key) {
  if (frame.set !== null) {
    requireValue(!frame.set.has(key), AGGREGATE.errors.input);
    frame.set.add(key);
    return;
  }
  requireValue(!frame.keys.includes(key), AGGREGATE.errors.input);
  frame.keys.push(key);
  if (frame.keys.length === LINEAR_KEYS) frame.set = new Set(frame.keys);
}

// Frames are reused per depth so a document of a million small objects allocates no per-object state.
function rejectDuplicateKeys(source) {
  const frames = Array.from({ length: MAX_DEPTH }, () => ({ object: false, property: false, keys: [], set: null }));
  let depth = 0;
  for (let index = 0; index < source.length; index++) {
    const code = source.charCodeAt(index);
    if (code === QUOTE) {
      const end = stringEnd(source, index);
      const frame = frames[depth - 1];
      if (depth > 0 && frame.property) {
        addKey(frame, keyAt(source, index, end));
        frame.property = false;
      }
      index = end;
    } else if (code === OPEN_OBJECT || code === OPEN_ARRAY) {
      requireValue(depth < MAX_DEPTH, AGGREGATE.errors.input);
      open(frames[depth++], code === OPEN_OBJECT);
    } else if (code === CLOSE_OBJECT || code === CLOSE_ARRAY) {
      if (depth > 0) depth--;
    } else if (code === COMMA && depth > 0) {
      frames[depth - 1].property = frames[depth - 1].object;
    }
  }
}

export function parseBytes(bytes) {
  const source = new TextDecoder(AGGREGATE.encoding, { fatal: true }).decode(bytes);
  rejectDuplicateKeys(source);
  return JSON.parse(source);
}
