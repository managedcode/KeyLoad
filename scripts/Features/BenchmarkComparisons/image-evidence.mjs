import { createHash } from 'node:crypto';
import { constants as fileFlags } from 'node:fs';
import { lstat, mkdir, open, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { directoryName, fileName, imageReference, message, outputFormat, processLimit, registryProbeErrorCodes, registryReadinessTokens, validation } from './image-contracts.mjs';

const privateDirectoryMode = 0o700;
const privateFileMode = 0o600;
const missingEntry = 'ENOENT';
const exclusiveWriteFlag = 'wx';
const replaceWriteFlag = 'w';
const appendWriteFlag = 'a';
const diagnosticRedactions = Object.freeze([
  Object.freeze({ pattern: /\bBearer\s+[A-Za-z0-9._~+/-]+=*/gi, replacement: 'Bearer [REDACTED]' }),
  Object.freeze({ pattern: /:\/\/[^/\s@]+@/g, replacement: '://[REDACTED]@' }),
  Object.freeze({
    pattern: /(\b(?:authorization|token|password|secret|credential|api[_-]?key)\s*[:=]\s*)(?:Bearer\s+)?[^\s,;]+/gi,
    replacement: '$1[REDACTED]',
  }),
  Object.freeze({ pattern: /\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16})\b/g, replacement: '[REDACTED]' }),
]);
const readinessOutcomes = Object.freeze(Object.values(registryReadinessTokens.outcome));
const readinessPhases = Object.freeze(Object.values(registryReadinessTokens.phase));
const utcTimestampPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/;

export async function ensureEvidenceDirectory(context) {
  const expectedPath = path.resolve(context.runnerTemp, directoryName.evidence);
  if (context.evidenceDirectory !== expectedPath || path.dirname(expectedPath) !== context.runnerTemp) {
    throw new Error(message.unsafeEvidencePath);
  }
  try {
    await mkdir(expectedPath, { mode: privateDirectoryMode });
  } catch (error) {
    if (error?.code !== 'EEXIST') throw new Error(message.unsafeEvidencePath);
  }
  const info = await lstat(expectedPath).catch(() => null);
  if (!info?.isDirectory() || info.isSymbolicLink() || (info.mode & 0o077) !== 0 || info.uid !== process.getuid()) {
    throw new Error(message.unsafeEvidencePath);
  }
  return expectedPath;
}

export async function createImmutableReceipt(context, receipt) {
  const target = await ownedPath(context, fileName.receipt);
  const handle = await open(target, exclusiveWriteFlag, privateFileMode).catch(error => {
    if (error?.code === 'EEXIST') throw new Error(message.evidenceExists);
    throw new Error(message.unsafeEvidencePath);
  });
  try {
    await handle.writeFile(`${JSON.stringify(receipt, null, outputFormat.jsonIndent)}${outputFormat.newline}`, outputFormat.utf8);
    await handle.sync();
  } finally {
    await handle.close();
  }
}

export async function createImmutableManifest(context, name, bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > processLimit.maxManifestBytes) {
    throw new Error(message.invalidManifest);
  }
  const target = await ownedPath(context, name);
  const handle = await open(target, exclusiveWriteFlag, privateFileMode).catch(error => {
    if (error?.code === 'EEXIST') throw new Error(message.evidenceExists);
    throw new Error(message.unsafeEvidencePath);
  });
  try {
    await handle.writeFile(bytes);
    await handle.sync();
  } finally {
    await handle.close();
  }
}

export async function writeCleanupCapture(context, logBytes, stateBytes) {
  if (!Buffer.isBuffer(logBytes) || logBytes.length > processLimit.maxLogBytes
    || !Buffer.isBuffer(stateBytes) || stateBytes.length > processLimit.maxInspectBytes) {
    throw new Error(message.commandOutputLimit);
  }
  const directory = await ensureEvidenceDirectory(context);
  const logPath = path.join(directory, fileName.registryLogs);
  const statePath = path.join(directory, fileName.registryState);
  await writeOwnedBytes(logPath, logBytes);
  await writeOwnedBytes(statePath, stateBytes);
}

export async function writeCleanupSummary(context, receipt) {
  const directory = await ensureEvidenceDirectory(context);
  const cleanupPath = path.join(directory, fileName.cleanupReceipt);
  await writeOwnedBytes(cleanupPath, Buffer.from(`${JSON.stringify(receipt, null, outputFormat.jsonIndent)}${outputFormat.newline}`, outputFormat.utf8));
}

export async function recordNativeCommand(context, operation, result, captureOutput = true) {
  if (!/^[a-z][a-z0-9-]{0,63}$/.test(operation)
    || !result || typeof result !== 'object'
    || typeof result.stdout !== 'string' || typeof result.stderr !== 'string') {
    throw new Error(message.commandOutputLimit);
  }
  const stdout = boundedSanitizedOutput(sanitizeNativeOutput(result.stdout), processLimit.maxOutputBytes);
  const stderr = boundedSanitizedOutput(sanitizeNativeOutput(result.stderr), processLimit.maxOutputBytes - stdout.bytes);
  const record = Object.freeze({
    operation,
    exitCode: Number.isInteger(result.code) ? result.code : null,
    signal: typeof result.signal === 'string' ? result.signal : null,
    timedOut: result.timedOut === true,
    outputLimitExceeded: result.outputLimitExceeded === true,
    spawnFailed: result.spawnFailed === true,
    outputTruncated: result.outputTruncated === true || stdout.truncated || stderr.truncated,
    stdoutBytes: Buffer.byteLength(stdout.text, outputFormat.utf8),
    stderrBytes: Buffer.byteLength(stderr.text, outputFormat.utf8),
    stdoutSha256: hashText(stdout.text),
    stderrSha256: hashText(stderr.text),
    stdoutBase64: captureOutput ? Buffer.from(stdout.text, outputFormat.utf8).toString(outputFormat.base64) : null,
    stderrBase64: captureOutput ? Buffer.from(stderr.text, outputFormat.utf8).toString(outputFormat.base64) : null,
  });
  await appendOwnedLine(context, fileName.nativeCommands, record, processLimit.maxNativeCommandRecords);
}

export function sanitizeNativeOutput(value) {
  if (typeof value !== 'string' || Buffer.byteLength(value, outputFormat.utf8) > processLimit.maxOutputBytes) {
    throw new Error(message.commandOutputLimit);
  }
  return diagnosticRedactions.reduce((sanitized, rule) => sanitized.replace(rule.pattern, rule.replacement), value);
}

function boundedSanitizedOutput(sanitized, maximumBytes) {
  const bytes = Buffer.from(sanitized, outputFormat.utf8);
  if (bytes.length <= maximumBytes) return Object.freeze({ text: sanitized, bytes: bytes.length, truncated: false });
  const safeEnd = Math.max(0, maximumBytes - 3);
  const text = bytes.subarray(0, safeEnd).toString(outputFormat.utf8);
  return Object.freeze({ text, bytes: Buffer.byteLength(text, outputFormat.utf8), truncated: true });
}

function hashText(value) {
  return createHash(outputFormat.sha256).update(value, outputFormat.utf8).digest(outputFormat.hex);
}

export async function recordRegistryHeaders(context, imageName, status, headers) {
  const safeContentType = typeof headers.contentType === 'string' ? headers.contentType.slice(0, 512) : null;
  const safeDigest = typeof headers.digest === 'string' && validation.digestPattern.test(headers.digest) ? headers.digest : null;
  const safeLength = typeof headers.contentLength === 'string' && /^\d{1,10}$/.test(headers.contentLength)
    ? headers.contentLength
    : null;
  const record = Object.freeze({ image: imageName, status: Number.isInteger(status) ? status : null,
    contentType: safeContentType, digest: safeDigest, contentLength: safeLength });
  await appendOwnedLine(context, fileName.registryHeaders, record, processLimit.maxNativeCommandRecords);
}

export async function recordRegistryReadiness(context, probe) {
  if (!probe || !Number.isInteger(probe.sequence) || probe.sequence < 1 || probe.sequence > processLimit.maxRegistryReadinessRecords
    || typeof probe.startedAt !== 'string' || !utcTimestampPattern.test(probe.startedAt) || !Number.isFinite(Date.parse(probe.startedAt))
    || !Number.isSafeInteger(probe.durationMs) || probe.durationMs < 0
    || !Number.isInteger(probe.timeoutMs) || probe.timeoutMs <= 0 || probe.timeoutMs > processLimit.readinessProbeTimeoutMs
    || (probe.status !== null && (!Number.isInteger(probe.status) || probe.status < 100 || probe.status > 599))
    || typeof probe.aborted !== 'boolean' || !readinessPhases.includes(probe.phase) || !readinessOutcomes.includes(probe.outcome)
    || (probe.errorCode !== null && !registryProbeErrorCodes.includes(probe.errorCode))) {
    throw new Error(message.commandOutputLimit);
  }
  const record = Object.freeze({ sequence: probe.sequence, startedAt: probe.startedAt, durationMs: probe.durationMs,
    timeoutMs: probe.timeoutMs, status: probe.status, aborted: probe.aborted, phase: probe.phase,
    outcome: probe.outcome, errorCode: probe.errorCode });
  await appendOwnedLine(context, fileName.registryReadiness, record, processLimit.maxRegistryReadinessRecords,
    processLimit.maxRegistryReadinessBytes);
}

export async function appendImageOutputs(context, serverReference, loadGeneratorReference) {
  const outputFilePath = context.githubOutput;
  const fileInfo = await lstat(outputFilePath).catch(() => null);
  if (!fileInfo?.isFile() || fileInfo.isSymbolicLink()) throw new Error(message.invalidEnvironment);
  for (const reference of loadGeneratorReference === undefined ? [serverReference] : [serverReference, loadGeneratorReference]) {
    if (typeof reference !== 'string' || validation.controlCharacterPattern.test(reference)) throw new Error(message.invalidManifest);
  }
  const content = `${imageReference.outputServer}=${serverReference}${outputFormat.newline}`
    + (loadGeneratorReference === undefined ? '' : `${imageReference.outputLoadGenerator}=${loadGeneratorReference}${outputFormat.newline}`);
  await writeFile(outputFilePath, content, { flag: appendWriteFlag, encoding: outputFormat.utf8, mode: privateFileMode });
}

async function writeOwnedBytes(target, bytes) {
  const directory = path.dirname(target);
  const info = await lstat(directory).catch(() => null);
  if (!info?.isDirectory() || info.isSymbolicLink()) throw new Error(message.unsafeEvidencePath);
  const existing = await lstat(target).catch(error => error?.code === missingEntry ? null : Promise.reject(error));
  if (existing && (!existing.isFile() || existing.isSymbolicLink())) throw new Error(message.unsafeEvidencePath);
  await writeFile(target, bytes, { flag: existing ? replaceWriteFlag : exclusiveWriteFlag, mode: privateFileMode });
}

async function appendOwnedLine(context, name, value, maximumRecords, maximumBytes = processLimit.maxCommandEvidenceBytes) {
  const target = await ownedPath(context, name);
  const line = Buffer.from(`${JSON.stringify(value)}${outputFormat.newline}`, outputFormat.utf8);
  if (line.length > processLimit.maxCommandEvidenceRecordBytes) throw new Error(message.commandOutputLimit);
  const flags = fileFlags.O_APPEND | fileFlags.O_CREAT | fileFlags.O_WRONLY | fileFlags.O_NOFOLLOW;
  let handle;
  try {
    handle = await open(target, flags, privateFileMode);
    const info = await handle.stat();
    if (!info.isFile() || info.uid !== process.getuid() || (info.mode & 0o077) !== 0) throw new Error(message.unsafeEvidencePath);
    const recordCount = (await readFile(target, outputFormat.utf8)).split(outputFormat.newline).filter(Boolean).length;
    if (info.size + line.length > maximumBytes || recordCount >= maximumRecords) {
      throw new Error(message.commandOutputLimit);
    }
    await handle.writeFile(line);
    await handle.sync();
  } catch (error) {
    if (error instanceof Error && [message.unsafeEvidencePath, message.commandOutputLimit].includes(error.message)) throw error;
    throw new Error(message.unsafeEvidencePath);
  } finally {
    await handle?.close();
  }
}

async function ownedPath(context, name) {
  const directory = await ensureEvidenceDirectory(context);
  const target = path.resolve(directory, name);
  if (path.dirname(target) !== directory) throw new Error(message.unsafeEvidencePath);
  return target;
}
