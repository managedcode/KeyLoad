import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdir, readFile, stat, symlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [modulePath, scenario, runnerTemp] = process.argv.slice(2);
const moduleDirectory = path.dirname(modulePath);
const { fileName, message, processLimit, registry } = await import(pathToFileURL(path.join(moduleDirectory, 'image-contracts.mjs')));
const { ensureEvidenceDirectory, recordRegistryReadiness } = await import(pathToFileURL(path.join(moduleDirectory, 'image-evidence.mjs')));
const { waitForRegistry } = await import(pathToFileURL(modulePath));
const context = Object.freeze({ runnerTemp, evidenceDirectory: path.join(runnerTemp, 'keyload-images') });
const readinessFile = path.join(context.evidenceDirectory, fileName.registryReadiness);
const privateBody = 'sensitive-fixture-body';
await mkdir(runnerTemp, { recursive: true, mode: 0o700 });
if (scenario === 'evidence-bounds') await assertEvidenceBounds();
else await runReadinessScenario();
process.stdout.write(`accepted:${scenario}\n`);

async function runReadinessScenario() {
  const fixture = makeFixture();
  try {
    await listenFixture(fixture);
    if (scenario === 'evidence-unsafe') await prepareUnsafeEvidence();
    const started = Date.now();
    let failure;
    try { await waitForRegistry(context); } catch (error) { failure = error; }
    const elapsedMs = Date.now() - started;
    if (scenario === 'evidence-unsafe') {
      assert.equal(failure?.message, message.unsafeEvidencePath);
      assert.equal(fixture.requests, 1);
      assert.equal(await readFile(path.join(runnerTemp, 'foreign-marker'), 'utf8'), 'preserve-me');
      return;
    }
    const text = await readFile(readinessFile, 'utf8');
    assert.equal(text.includes(privateBody), false);
    const rows = text.trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
    await assertPrivateBounded(rows);
    if (scenario === 'status-deadline' || scenario === 'hung-deadline') assertDeadline(failure, elapsedMs, rows);
    else assertSuccessfulReadiness(failure, elapsedMs, rows, fixture);
  } finally {
    await closeFixture(fixture);
  }
}

function makeFixture() {
  const fixture = { requests: 0, closedRequests: 0, listening: false, timers: new Set(), sockets: new Set() };
  fixture.server = createServer((request, response) => serveRequest(fixture, request, response));
  fixture.server.on('connection', socket => {
    fixture.sockets.add(socket);
    socket.once('close', () => fixture.sockets.delete(socket));
  });
  return fixture;
}

function serveRequest(fixture, request, response) {
  fixture.requests++;
  assert.equal(request.method, 'GET');
  assert.equal(request.url, '/v2/');
  request.once('close', () => fixture.closedRequests++);
  if (scenario === 'hung-first' && fixture.requests === 1 || scenario === 'hung-deadline') return;
  if (scenario === 'reset-first' && fixture.requests === 1) { request.socket.destroy(); return; }
  if (scenario === 'slow-first' && fixture.requests === 1) {
    const timer = setTimeout(() => { fixture.timers.delete(timer); response.writeHead(200); response.end(privateBody); }, 2700);
    fixture.timers.add(timer);
    response.once('close', () => { clearTimeout(timer); fixture.timers.delete(timer); });
    return;
  }
  if (scenario === 'redirect-first' && fixture.requests === 1) {
    response.writeHead(302, { Location: '/redirected' }); response.end(privateBody); return;
  }
  const status = scenario === 'status-deadline' || scenario === 'status-first' && fixture.requests === 1 ? 503 : 200;
  response.writeHead(status);
  response.end(privateBody);
}

async function listenFixture(fixture) {
  await new Promise((resolve, reject) => {
    fixture.server.once('error', reject);
    fixture.server.listen(registry.port, registry.host, () => {
      fixture.server.removeListener('error', reject);
      fixture.listening = true;
      resolve();
    });
  });
}

async function closeFixture(fixture) {
  for (const timer of fixture.timers) clearTimeout(timer);
  for (const socket of fixture.sockets) socket.destroy();
  if (fixture.listening) {
    await new Promise((resolve, reject) => fixture.server.close(error => error ? reject(error) : resolve()));
  }
}

async function prepareUnsafeEvidence() {
  await ensureEvidenceDirectory(context);
  const marker = path.join(runnerTemp, 'foreign-marker');
  await writeFile(marker, 'preserve-me', { mode: 0o600 });
  await symlink(marker, readinessFile);
}

async function assertPrivateBounded(rows) {
  assert(rows.length > 0 && rows.length <= processLimit.maxRegistryReadinessRecords);
  assert((await stat(readinessFile)).size <= processLimit.maxRegistryReadinessBytes);
  assert.equal((await stat(readinessFile)).mode & 0o777, 0o600);
  assert.equal((await stat(context.evidenceDirectory)).mode & 0o777, 0o700);
  const fields = ['sequence', 'startedAt', 'durationMs', 'timeoutMs', 'status', 'aborted', 'phase', 'outcome', 'errorCode'];
  for (const [index, row] of rows.entries()) {
    assert.equal(row.sequence, index + 1);
    assert.deepEqual(Object.keys(row), fields);
    assert(row.timeoutMs > 0 && row.timeoutMs <= 2000);
    assert(row.durationMs >= 0);
  }
}

function assertDeadline(failure, elapsedMs, rows) {
  assert.equal(failure?.message, message.registryTimeout);
  assert(elapsedMs >= 30000 && elapsedMs < 32000);
  assert.equal(rows.some(row => row.outcome === 'ready'), false);
  if (scenario === 'status-deadline') {
    assert(rows.every(row => row.status === 503 && row.outcome === 'http-status'));
  } else {
    assert(rows.every(row => row.status === null && row.aborted && row.outcome === 'timeout'));
    assert(rows.at(-1).timeoutMs < 2000);
  }
}

function assertSuccessfulReadiness(failure, elapsedMs, rows, fixture) {
  assert.equal(failure, undefined);
  assert.equal(rows.at(-1).status, 200);
  assert.equal(rows.at(-1).aborted, false);
  assert.equal(rows.at(-1).phase, 'body-cancel');
  assert.equal(rows.at(-1).outcome, 'ready');
  if (scenario === 'healthy') assert.equal(rows.length, 1);
  else assert(rows.length > 1);
  if (scenario === 'hung-first' || scenario === 'slow-first') {
    assert.equal(rows[0].outcome, 'timeout');
    assert.equal(rows[0].status, null);
    assert.equal(rows[0].aborted, true);
    assert(rows[0].durationMs >= 1900 && elapsedMs < 6000);
    assert(fixture.closedRequests >= 1);
  }
  if (scenario === 'status-first') assert.equal(rows[0].status, 503);
  if (scenario === 'reset-first') assert.equal(rows[0].errorCode, 'UND_ERR_SOCKET');
  if (scenario === 'redirect-first') {
    assert.equal(rows[0].outcome, 'request-failed');
    assert.equal(rows[0].errorCode, null);
    assert.equal(fixture.requests, 2);
  }
}

async function assertEvidenceBounds() {
  const valid = Object.freeze({ sequence: 1, startedAt: '2026-10-04T00:00:00.000Z', durationMs: 12,
    timeoutMs: 2000, status: 200, aborted: false, phase: 'body-cancel', outcome: 'ready', errorCode: null });
  for (const input of invalidEvidence(valid)) {
    await assert.rejects(recordRegistryReadiness(context, input), { message: message.commandOutputLimit });
  }
  await recordRegistryReadiness(context, { ...valid, message: 'must-not-leak', body: 'must-not-leak', env: 'must-not-leak' });
  assert.equal((await readFile(readinessFile, 'utf8')).includes('must-not-leak'), false);
  for (let sequence = 2; sequence <= processLimit.maxRegistryReadinessRecords; sequence++) {
    await recordRegistryReadiness(context, { ...valid, sequence });
  }
  const initial = await readFile(readinessFile, 'utf8');
  assert.equal(initial.trim().split('\n').length, 121);
  await assert.rejects(recordRegistryReadiness(context, valid), { message: message.commandOutputLimit });
  assert.equal(await readFile(readinessFile, 'utf8'), initial);
  assert((await stat(readinessFile)).size < 64 * 1024);
  await assertEvidenceByteLimit(valid);
}

async function assertEvidenceByteLimit(valid) {
  const overflowTemp = path.join(context.runnerTemp, 'evidence-bytes');
  await mkdir(overflowTemp, { recursive: true, mode: 0o700 });
  const overflow = { runnerTemp: overflowTemp, evidenceDirectory: path.join(overflowTemp, 'keyload-images') };
  await ensureEvidenceDirectory(overflow);
  const overflowFile = path.join(overflow.evidenceDirectory, fileName.registryReadiness);
  await writeFile(overflowFile, ' '.repeat(processLimit.maxRegistryReadinessBytes), { mode: 0o600 });
  await assert.rejects(recordRegistryReadiness(overflow, valid), { message: message.commandOutputLimit });
  assert.equal((await stat(overflowFile)).size, processLimit.maxRegistryReadinessBytes);
}

function invalidEvidence(valid) {
  return [null, {}, { ...valid, sequence: 0 }, { ...valid, sequence: 122 }, { ...valid, sequence: 1.5 },
    { ...valid, startedAt: 'bad' }, { ...valid, startedAt: '2026-99-99T00:00:00.000Z' },
    { ...valid, durationMs: -1 }, { ...valid, durationMs: Number.POSITIVE_INFINITY },
    { ...valid, timeoutMs: 0 }, { ...valid, timeoutMs: 2001 }, { ...valid, timeoutMs: 0.5 },
    { ...valid, status: 0 }, { ...valid, status: 600 }, { ...valid, status: '200' },
    { ...valid, aborted: 'false' }, { ...valid, phase: 'raw message' },
    { ...valid, outcome: 'raw message' }, { ...valid, errorCode: 'secret=preserve-secret' }];
}
