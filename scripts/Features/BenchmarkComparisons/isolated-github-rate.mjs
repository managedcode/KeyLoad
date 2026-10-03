import { randomInt } from 'node:crypto';
import { GH, requireGitHub } from './isolated-github-contract.mjs';

const transientStatuses = Object.freeze([500, 502, 503, 504]);
const transientInitialDelayMs = 5000;
const transientMaximumJitterMs = 1000;

function retryAfter(value, nowMs) {
  if (/^\d+$/.test(value)) {
    const seconds = Number(value);
    requireGitHub(Number.isSafeInteger(seconds));
    return seconds * 1000;
  }
  requireGitHub(/^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} GMT$/.test(value));
  const time = Date.parse(value);
  requireGitHub(Number.isFinite(time));
  return Math.max(0, time - nowMs);
}

export function retryDelay(response, nowMs, waitedMs, repeats) {
  const transient = transientStatuses.includes(response?.status);
  requireGitHub(([403, 429].includes(response?.status) || transient) && Number.isSafeInteger(nowMs)
    && Number.isSafeInteger(waitedMs) && waitedMs >= 0 && Number.isSafeInteger(repeats) && repeats >= 0 && repeats < GH.rateRepeats);
  const headers = response.headers;
  requireGitHub(headers !== null && typeof headers === 'object' && !Array.isArray(headers));
  const waits = [];
  if (Object.hasOwn(headers, 'retry-after')) waits.push(retryAfter(headers['retry-after'], nowMs));
  if (headers['x-ratelimit-remaining'] === '0') {
    const reset = headers['x-ratelimit-reset'];
    requireGitHub(typeof reset === 'string' && /^\d+$/.test(reset) && Number.isSafeInteger(Number(reset)) && Number(reset) > 0);
    waits.push(Math.max(0, Number(reset) * 1000 - nowMs));
  }
  requireGitHub(transient || waits.length > 0);
  const providerDelay = waits.length > 0 ? Math.max(1000, Math.ceil(Math.max(...waits) / 1000) * 1000 + 1000) : 0;
  const transientDelay = transient
    ? transientInitialDelayMs * 2 ** repeats + randomInt(transientMaximumJitterMs + 1) : 0;
  const delay = Math.max(providerDelay, transientDelay);
  requireGitHub(Number.isSafeInteger(delay) && waitedMs + delay <= GH.rateWaitMs);
  return delay;
}
