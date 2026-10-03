const RELEASE_REPOSITORY = 'managedcode/KeyLoad';
const SCHEMA_VERSION = 1;
const MAX_VERSION_COMPONENT = 65534;
const MAX_INVENTORY_ITEMS = 10000;
const RESERVATION_KEYS = Object.freeze(['schemaVersion', 'repository', 'sourceRevision', 'runId', 'baseVersion',
  'major', 'minor', 'date', 'sequence', 'version', 'tag', 'assemblyVersion', 'fileVersion']);

export const RELEASE_VERSION_ERRORS = Object.freeze({
  input: 'E_RELEASE_INPUT', date: 'E_RELEASE_DATE', dailyRuns: 'E_RELEASE_DAILY_RUNS', tags: 'E_RELEASE_TAGS',
  exhausted: 'E_RELEASE_SEQUENCE_EXHAUSTED', reservation: 'E_RELEASE_RESERVATION',
});

export class ReleaseVersionError extends Error {
  constructor(code, message) { super(message); this.name = 'ReleaseVersionError'; this.code = code; }
}

function reject(code, message) { throw new ReleaseVersionError(code, message); }
function plainObject(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function positiveInteger(value) { return Number.isSafeInteger(value) && value > 0; }

function normalizeRunId(value, code) {
  if (positiveInteger(value)) return String(value);
  if (typeof value === 'string' && value.length <= 20 && /^[1-9][0-9]*$/.test(value)) return value;
  reject(code, 'A canonical positive run id is required.');
}

function parseBaseVersion(value) {
  if (typeof value !== 'string') reject(RELEASE_VERSION_ERRORS.input, 'A canonical M.m.0-dev base version is required.');
  const match = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.0-dev$/.exec(value);
  if (!match) reject(RELEASE_VERSION_ERRORS.input, 'A canonical M.m.0-dev base version is required.');
  const major = Number(match[1]);
  const minor = Number(match[2]);
  if (major > MAX_VERSION_COMPONENT || minor > MAX_VERSION_COMPONENT) {
    reject(RELEASE_VERSION_ERRORS.input, 'Major and minor versions must fit CLR metadata components.');
  }
  return { major, minor };
}

function parseUtcDate(value, code) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z$/.test(value)) {
    reject(code, 'An authenticated UTC timestamp ending in Z is required.');
  }
  const parsed = Date.parse(value);
  const datePart = value.slice(0, 10);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString().slice(0, 10) !== datePart) {
    reject(code, 'The UTC timestamp is not a valid calendar date.');
  }
  return datePart.slice(2, 4) + datePart.slice(5, 7) + datePart.slice(8, 10);
}

function inventoryDate(run) {
  return parseUtcDate(run.created_at, RELEASE_VERSION_ERRORS.dailyRuns);
}

function dailyOrdinal(dailyRuns, currentRunId, date) {
  if (!Array.isArray(dailyRuns) || dailyRuns.length === 0 || dailyRuns.length > MAX_INVENTORY_ITEMS) {
    reject(RELEASE_VERSION_ERRORS.dailyRuns, 'The bounded release-run inventory is invalid.');
  }
  const ids = new Set();
  const numbers = new Set();
  const todays = [];
  for (const run of dailyRuns) {
    if (!plainObject(run) || !positiveInteger(run.run_number)) reject(RELEASE_VERSION_ERRORS.dailyRuns, 'A release-run inventory entry is invalid.');
    const id = normalizeRunId(run.id, RELEASE_VERSION_ERRORS.dailyRuns);
    const runDate = inventoryDate(run);
    if (ids.has(id) || numbers.has(run.run_number)) reject(RELEASE_VERSION_ERRORS.dailyRuns, 'Release-run identities must be unique.');
    ids.add(id);
    numbers.add(run.run_number);
    if (runDate === date) todays.push({ id, runNumber: run.run_number });
  }
  todays.sort((left, right) => left.runNumber - right.runNumber);
  const matches = todays.flatMap((run, index) => run.id === currentRunId ? [index + 1] : []);
  if (matches.length !== 1) reject(RELEASE_VERSION_ERRORS.dailyRuns, 'The current release run must occur once in its UTC-day inventory.');
  return matches[0];
}

function nextTagSequence(tags, major, minor, date) {
  if (!Array.isArray(tags) || tags.length > MAX_INVENTORY_ITEMS) reject(RELEASE_VERSION_ERRORS.tags, 'The bounded Git tag inventory is invalid.');
  const seen = new Set();
  let maximum = 0;
  const prefix = `v${major}.${minor}.${date}.`;
  for (const tag of tags) {
    if (typeof tag !== 'string' || tag.length === 0 || tag.length > 128 || seen.has(tag)) {
      reject(RELEASE_VERSION_ERRORS.tags, 'Git tag names must be bounded, nonempty, and unique.');
    }
    seen.add(tag);
    if (!tag.startsWith(prefix)) continue;
    const match = /^([1-9][0-9]*)$/.exec(tag.slice(prefix.length));
    if (!match) reject(RELEASE_VERSION_ERRORS.tags, 'A dated release tag has a noncanonical sequence.');
    const counter = BigInt(match[1]);
    if (counter > BigInt(MAX_VERSION_COMPONENT)) reject(RELEASE_VERSION_ERRORS.exhausted, 'The dated release sequence is exhausted.');
    maximum = Math.max(maximum, Number(counter));
  }
  return maximum + 1;
}

function makeReservation({ baseVersion, sourceRevision, runId, major, minor, date, sequence }) {
  if (!Number.isSafeInteger(sequence) || sequence < 1 || sequence > MAX_VERSION_COMPONENT) {
    reject(RELEASE_VERSION_ERRORS.exhausted, 'The dated release sequence is outside the supported range.');
  }
  const version = `${major}.${minor}.${date}.${sequence}`;
  return {
    schemaVersion: SCHEMA_VERSION, repository: RELEASE_REPOSITORY, sourceRevision, runId, baseVersion,
    major, minor, date, sequence, version, tag: `v${version}`,
    assemblyVersion: `${major}.${minor}.0.0`, fileVersion: `${major}.${minor}.0.${sequence}`,
  };
}

function validateReservation(value, identity, ordinal) {
  if (!plainObject(value) || Object.keys(value).length !== RESERVATION_KEYS.length ||
      RESERVATION_KEYS.some(key => !Object.hasOwn(value, key))) {
    reject(RELEASE_VERSION_ERRORS.reservation, 'The existing release reservation has an invalid shape.');
  }
  if (!positiveInteger(value.sequence) || value.sequence < ordinal || value.sequence > MAX_VERSION_COMPONENT) {
    reject(RELEASE_VERSION_ERRORS.reservation, 'The existing release reservation has an invalid daily sequence.');
  }
  const expected = makeReservation({ ...identity, sequence: value.sequence });
  if (RESERVATION_KEYS.some(key => value[key] !== expected[key])) {
    reject(RELEASE_VERSION_ERRORS.reservation, 'The existing release reservation belongs to another identity or version.');
  }
  return expected;
}

export function resolveReleaseVersion(input = {}) {
  if (!plainObject(input)) reject(RELEASE_VERSION_ERRORS.input, 'A release version input object is required.');
  const { baseVersion, sourceRevision, runId, utcTimestamp, tags, dailyRuns, reservation = null } = input;
  const { major, minor } = parseBaseVersion(baseVersion);
  if (typeof sourceRevision !== 'string' || !/^[a-f0-9]{40}$/.test(sourceRevision)) {
    reject(RELEASE_VERSION_ERRORS.input, 'A full lowercase source commit SHA is required.');
  }
  const normalizedRunId = normalizeRunId(runId, RELEASE_VERSION_ERRORS.input);
  const date = parseUtcDate(utcTimestamp, RELEASE_VERSION_ERRORS.date);
  const ordinal = dailyOrdinal(dailyRuns, normalizedRunId, date);
  const identity = { baseVersion, sourceRevision, runId: normalizedRunId, major, minor, date };
  if (reservation !== null) return validateReservation(reservation, identity, ordinal);
  const sequence = Math.max(ordinal, nextTagSequence(tags, major, minor, date));
  return makeReservation({ ...identity, sequence });
}
