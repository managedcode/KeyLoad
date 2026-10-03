import { TextDecoder } from 'node:util';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';

const TOKEN = /\s*("(?:[^"\\\x00-\x1F]|\\(?:["\\\x2Fbfnrt]|u[a-fA-F0-9]{4}))*"|[{}\[\],:]|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/y;
const MAX_DEPTH = 64;

function rejectDuplicateKeys(source) {
  const frames = [];
  let position = 0;
  while (position < source.length) {
    TOKEN.lastIndex = position;
    const match = TOKEN.exec(source);
    if (match === null) {
      requireValue(source.slice(position).trim().length === 0, AGGREGATE.errors.input);
      return;
    }
    position = TOKEN.lastIndex;
    const token = match[1];
    const frame = frames.at(-1);
    if (token === '{' || token === '[') {
      frames.push(token === '{' ? { keys: new Set(), property: true } : null);
      requireValue(frames.length <= MAX_DEPTH, AGGREGATE.errors.input);
    } else if (token === '}' || token === ']') {
      frames.pop();
    } else if (token === ',' && frame) {
      frame.property = true;
    } else if (token.startsWith('"') && frame?.property) {
      const key = JSON.parse(token);
      requireValue(!frame.keys.has(key), AGGREGATE.errors.input);
      frame.keys.add(key);
      frame.property = false;
    }
  }
}

export function parseBytes(bytes) {
  const source = new TextDecoder(AGGREGATE.encoding, { fatal: true }).decode(bytes);
  rejectDuplicateKeys(source);
  return JSON.parse(source);
}
