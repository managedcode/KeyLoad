import { TextDecoder } from 'node:util';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';

const TOKEN = /["{}\[\],:]/g;
const MAX_DEPTH = 64;

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

function rejectDuplicateKeys(source) {
  const frames = [];
  TOKEN.lastIndex = 0;
  let match;
  while ((match = TOKEN.exec(source)) !== null) {
    const token = match[0];
    const frame = frames.at(-1);
    if (token === '"') {
      const end = stringEnd(source, match.index);
      TOKEN.lastIndex = end + 1;
      if (frame?.property) {
        const literal = source.slice(match.index + 1, end);
        const key = literal.includes('\\') ? JSON.parse(source.slice(match.index, end + 1)) : literal;
        requireValue(!frame.keys.has(key), AGGREGATE.errors.input);
        frame.keys.add(key);
        frame.property = false;
      }
    } else if (token === '{' || token === '[') {
      frames.push(token === '{' ? { keys: new Set(), property: true } : null);
      requireValue(frames.length <= MAX_DEPTH, AGGREGATE.errors.input);
    } else if (token === '}' || token === ']') {
      frames.pop();
    } else if (token === ',' && frame) {
      frame.property = true;
    }
  }
}

export function parseBytes(bytes) {
  const source = new TextDecoder(AGGREGATE.encoding, { fatal: true }).decode(bytes);
  rejectDuplicateKeys(source);
  return JSON.parse(source);
}
