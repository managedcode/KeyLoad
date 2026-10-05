import { lstat, mkdir, open, rename, writeFile } from 'node:fs/promises';
import { constants } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { readBuildInputs } from './local-image-context.mjs';
import { localImage, messages } from './local-image-contracts.mjs';
import { ensureSuccess, runDocker } from './local-image-process.mjs';

const expectedReceiptFields = Object.freeze([
  'schemaVersion', 'state', 'provenance', 'invocationId', 'imageReference', 'inputDigest', 'imageConfigId',
  'sourceLabel', 'invocationLabel', 'pinnedBaseImages', 'producerVersion',
]);
const tagPattern = /^local-[a-f0-9]{32}$/u;
const digestPattern = /^sha256:[a-f0-9]{64}$/u;
const imageIdPattern = /^sha256:[a-f0-9]{64}$/u;
const containerIdPattern = /^[a-f0-9]{64}$/iu;
const unixSocketPattern = /^unix:\/\//u;
const imageRepository = localImage.imageRepository;

export async function runLocalImageAction(action, tag, receiptPath, root = process.cwd()) {
  validateAction(action, tag);
  const ownedReceiptPath = validateReceiptPath(root, receiptPath, tag);
  if (action === 'prepare') return prepare(root, tag, ownedReceiptPath);
  if (action === 'verify') return verify(root, tag, ownedReceiptPath);
  if (action === 'cleanup') return cleanup(root, tag, ownedReceiptPath);
  throw new Error(messages.invalidArguments);
}

async function prepare(root, tag, receiptPath) {
  await ensureLocalDocker(root);
  const inputs = await readBuildInputs(root);
  const invocationId = tag.slice(localImage.imageTagPrefix.length);
  if ((await readImageIds(root, imageReference(tag))).length !== 0
    || await lstat(receiptPath).then(() => true, () => false)) {
    throw new Error(messages.imageMismatch);
  }
  let receipt = createReceipt(tag, invocationId, inputs, localImage.buildingState, '');
  await writeReceipt(receiptPath, receipt);
  const reference = imageReference(tag);
  const build = await runDocker(root, [localImage.dockerImage, localImage.build, '--file', 'Dockerfile', '--tag', reference,
    '--label', localImage.sourceContextLabelPrefix + inputs.digest,
    '--label', localImage.invocationLabelPrefix + invocationId, '.'], localImage.buildTimeoutMs, false);
  if (build.code !== 0) {
    const present = await readImageIds(root, reference);
    if (present.length === 0) {
      receipt = createReceipt(tag, invocationId, inputs, localImage.absentState, '');
      await writeReceipt(receiptPath, receipt);
      throw new Error(messages.imageBuild);
    }
  }
  const actual = await inspectImage(root, reference);
  validateImage(actual, inputs.digest, invocationId);
  receipt = createReceipt(tag, invocationId, inputs, localImage.readyState, actual.imageConfigId);
  await writeReceipt(receiptPath, receipt);
  if (build.code !== 0) throw new Error(messages.imageBuild);
}

async function verify(root, tag, receiptPath) {
  await ensureLocalDocker(root);
  const receipt = await readReceipt(root, receiptPath, tag);
  if (receipt.state !== localImage.readyState) throw new Error(messages.invalidReceipt);
  const inputs = await readBuildInputs(root);
  if (inputs.digest !== receipt.inputDigest || !sameArray(inputs.bases, receipt.pinnedBaseImages)) {
    throw new Error(messages.contextChanged);
  }
  const actual = await inspectImage(root, imageReference(tag));
  validateImage(actual, inputs.digest, receipt.invocationId);
  if (actual.imageConfigId !== receipt.imageConfigId) throw new Error(messages.imageMismatch);
  return Object.freeze({ provenance: receipt.provenance, imageReference: receipt.imageReference,
    invocationId: receipt.invocationId, inputDigest: receipt.inputDigest, imageConfigId: receipt.imageConfigId });
}

async function cleanup(root, tag, receiptPath) {
  await ensureLocalDocker(root);
  const reference = imageReference(tag);
  const ids = await readImageIds(root, reference);
  if (ids.length === 0) {
    const receiptPresent = await lstat(receiptPath).then(() => true, error => {
      if (error?.code === 'ENOENT') return false;
      throw error;
    });
    if (!receiptPresent) return;
    const retained = await readReceipt(root, receiptPath, tag);
    if (retained.state === localImage.readyState) {
      await requireNoOwnedContainers(root, retained.imageConfigId);
    }
    return;
  }
  const receipt = await readReceipt(root, receiptPath, tag);
  if (receipt.state === localImage.absentState) throw new Error(messages.invalidReceipt);
  const inputs = await readBuildInputs(root);
  if (inputs.digest !== receipt.inputDigest || !sameArray(inputs.bases, receipt.pinnedBaseImages)) {
    throw new Error(messages.contextChanged);
  }
  if (ids.length !== 1) throw new Error(messages.dockerIdentity);
  const actual = await inspectImage(root, reference);
  validateImage(actual, inputs.digest, receipt.invocationId);
  if (receipt.state === localImage.readyState && actual.imageConfigId !== receipt.imageConfigId) {
    throw new Error(messages.imageMismatch);
  }
  await requireNoOwnedContainers(root, actual.imageConfigId);
  ensureSuccess(await runDocker(root, [localImage.dockerImage, localImage.remove, reference],
    localImage.commandTimeoutMs), messages.imageCleanup);
  if ((await readImageIds(root, reference)).length !== 0) throw new Error(messages.imageCleanup);
}

function createReceipt(tag, invocationId, inputs, state, imageConfigId) {
  return Object.freeze({
    schemaVersion: localImage.schemaVersion,
    state,
    provenance: localImage.provenance,
    invocationId,
    imageReference: imageReference(tag),
    inputDigest: inputs.digest,
    imageConfigId: imageConfigId || null,
    sourceLabel: localImage.sourceLabel,
    invocationLabel: localImage.invocationLabel,
    pinnedBaseImages: inputs.bases,
    producerVersion: localImage.producerVersion,
  });
}

async function inspectImage(root, reference) {
  const result = ensureSuccess(await runDocker(root, [localImage.dockerImage, localImage.inspect,
    '--format', localImage.imageInspectFormat, reference], localImage.commandTimeoutMs), messages.dockerIdentity);
  const fields = result.stdout.trim().split('|');
  if (fields.length !== 3 || !imageIdPattern.test(fields[0]) || !digestPattern.test(fields[1])
    || !tagPattern.test(localImage.imageTagPrefix + fields[2])) throw new Error(messages.dockerIdentity);
  return Object.freeze({ imageConfigId: fields[0], inputDigest: fields[1], invocationId: fields[2] });
}

function validateImage(actual, inputDigest, invocationId) {
  if (actual.inputDigest !== inputDigest || actual.invocationId !== invocationId
    || !imageIdPattern.test(actual.imageConfigId)) throw new Error(messages.imageMismatch);
}

async function readImageIds(root, reference) {
  const result = ensureSuccess(await runDocker(root, [localImage.dockerImage, 'ls',
    localImage.quiet, '--no-trunc', reference], localImage.commandTimeoutMs), messages.dockerIdentity);
  const lines = result.stdout.split(/\r?\n/u).filter(Boolean);
  if (lines.some(line => !imageIdPattern.test(line)) || lines.length > 1) throw new Error(messages.dockerIdentity);
  return lines;
}

async function requireNoOwnedContainers(root, imageConfigId) {
  const result = ensureSuccess(await runDocker(root, [localImage.docker, 'ps', localImage.all, '--no-trunc', localImage.filter,
    `ancestor=${imageConfigId}`, localImage.format, localImage.containersFormat], localImage.commandTimeoutMs), messages.imageCleanup);
  const lines = result.stdout.split(/\r?\n/u).filter(Boolean);
  if (lines.length > localImage.maxContainerRecords) throw new Error(messages.imageCleanup);
  for (const line of lines) {
    const [id, state] = line.split('|');
    if (!containerIdPattern.test(id ?? '') || !state) throw new Error(messages.dockerIdentity);
    if (state.toLowerCase() === 'running' || state.toLowerCase() === 'created'
      || state.toLowerCase() === 'restarting' || state.toLowerCase() === 'paused') {
      throw new Error(messages.imageInUse);
    }
  }
  if (lines.length !== 0) throw new Error(messages.imageInUse);
}

async function ensureLocalDocker(root) {
  const contextName = ensureSuccess(await runDocker(root, ['context', 'show'], localImage.commandTimeoutMs),
    messages.dockerUnavailable).stdout.trim();
  if (!contextName || /[\r\n]/u.test(contextName)) throw new Error(messages.dockerIdentity);
  const endpoint = ensureSuccess(await runDocker(root, ['context', 'inspect', contextName, '--format',
    '{{(index .Endpoints "docker").Host}}'], localImage.commandTimeoutMs), messages.dockerIdentity).stdout.trim();
  if (!unixSocketPattern.test(endpoint) || (process.env.DOCKER_HOST && process.env.DOCKER_HOST !== endpoint)) {
    throw new Error(messages.dockerIdentity);
  }
  const os = ensureSuccess(await runDocker(root, ['info', '--format', '{{.OSType}}'], localImage.commandTimeoutMs),
    messages.dockerUnavailable).stdout.trim();
  if (os !== 'linux') throw new Error(messages.dockerIdentity);
}

async function readReceipt(root, pathname, tag) {
  await verifyExistingReceiptDirectory(root, pathname);
  let handle;
  let bytes;
  try {
    const before = await lstat(pathname);
    if (!before.isFile() || before.isSymbolicLink() || before.size === 0
      || before.size > localImage.maxReceiptBytes) throw new Error(messages.invalidReceipt);
    handle = await open(pathname, constants.O_RDONLY | constants.O_NOFOLLOW);
    const opened = await handle.stat();
    if (!opened.isFile() || opened.size !== before.size || opened.ino !== before.ino) {
      throw new Error(messages.invalidReceipt);
    }
    const bounded = Buffer.allocUnsafe(localImage.maxReceiptBytes + 1);
    const { bytesRead } = await handle.read(bounded, 0, bounded.length, 0);
    const after = await handle.stat();
    const current = await lstat(pathname);
    if (bytesRead !== before.size || bytesRead > localImage.maxReceiptBytes
      || after.size !== before.size || current.ino !== before.ino || current.size !== before.size
      || !current.isFile() || current.isSymbolicLink()) {
      throw new Error(messages.invalidReceipt);
    }
    bytes = bounded.subarray(0, bytesRead);
  } catch (error) {
    if (error?.message === messages.invalidReceipt) throw error;
    throw new Error(messages.invalidReceipt);
  } finally {
    await handle?.close();
  }
  const raw = bytes.toString('utf8');
  let parsed;
  try { parsed = JSON.parse(raw); }
  catch { throw new Error(messages.invalidReceipt); }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error(messages.invalidReceipt);
  for (const field of expectedReceiptFields) {
    if ([...raw.matchAll(new RegExp(`\"${field}\"\\s*:`, 'gu'))].length !== 1) {
      throw new Error(messages.invalidReceipt);
    }
  }
  const keys = Object.keys(parsed).sort();
  const expected = [...expectedReceiptFields].sort();
  if (!sameArray(keys, expected) || parsed.schemaVersion !== localImage.schemaVersion
    || ![localImage.readyState, localImage.absentState, localImage.buildingState].includes(parsed.state)
    || parsed.provenance !== localImage.provenance || parsed.invocationId !== tag.slice(localImage.imageTagPrefix.length)
    || parsed.imageReference !== imageReference(tag) || !digestPattern.test(parsed.inputDigest)
    || parsed.sourceLabel !== localImage.sourceLabel || parsed.invocationLabel !== localImage.invocationLabel
    || parsed.producerVersion !== localImage.producerVersion || !Array.isArray(parsed.pinnedBaseImages)
    || parsed.pinnedBaseImages.length !== 2 || parsed.pinnedBaseImages.some(value => typeof value !== 'string')) {
    throw new Error(messages.invalidReceipt);
  }
  if (parsed.state === localImage.readyState && !imageIdPattern.test(parsed.imageConfigId ?? '')) {
    throw new Error(messages.invalidReceipt);
  }
  if (parsed.state !== localImage.readyState && parsed.imageConfigId !== null) throw new Error(messages.invalidReceipt);
  return parsed;
}

async function verifyExistingReceiptDirectory(root, receiptPath) {
  const parent = path.dirname(receiptPath);
  root = path.resolve(root);
  const relative = path.relative(root, parent);
  if (relative.startsWith('..') || path.isAbsolute(relative)) throw new Error(messages.invalidReceiptPath);
  let current = root;
  for (const segment of relative.split(path.sep).filter(Boolean)) {
    current = path.join(current, segment);
    const info = await lstat(current).catch(() => null);
    if (!info?.isDirectory() || info.isSymbolicLink()) throw new Error(messages.invalidReceiptPath);
  }
}

async function writeReceipt(pathname, receipt) {
  const parent = path.dirname(pathname);
  await ensureOwnedDirectory(parent);
  const temporary = pathname + '.' + receipt.invocationId + '.tmp';
  const bytes = Buffer.from(JSON.stringify(receipt) + '\n', 'utf8');
  if (bytes.length > localImage.maxReceiptBytes) throw new Error(messages.invalidReceipt);
  await writeFile(temporary, bytes, { flag: 'wx', mode: 0o600 });
  await rename(temporary, pathname);
}

async function ensureOwnedDirectory(directory) {
  const segments = path.resolve(directory).split(path.sep).filter(Boolean);
  let current = path.parse(directory).root;
  for (const segment of segments) {
    current = path.join(current, segment);
    await mkdir(current, { mode: 0o700 }).catch(error => { if (error?.code !== 'EEXIST') throw error; });
    const info = await lstat(current);
    if (!info.isDirectory() || info.isSymbolicLink()) throw new Error(messages.invalidReceiptPath);
  }
}

function validateAction(action, tag) {
  if (!['prepare', 'verify', 'cleanup'].includes(action) || !tagPattern.test(tag ?? '')
    || tag.length > localImage.maxTagCharacters) throw new Error(messages.invalidInvocation);
}

function validateReceiptPath(root, receiptPath, tag) {
  const expected = path.resolve(root, localImage.receiptDirectory,
    localImage.receiptNamePrefix + tag.slice(localImage.imageTagPrefix.length) + localImage.receiptNameSuffix);
  const actual = path.resolve(root, receiptPath ?? '');
  if (actual !== expected || path.relative(root, actual).startsWith('..')) throw new Error(messages.invalidReceiptPath);
  return actual;
}

function imageReference(tag) { return `${imageRepository}:${tag}`; }
function sameArray(left, right) { return left.length === right.length && left.every((value, index) => value === right[index]); }

async function main(argv) {
  if (argv.length !== 3) throw new Error(messages.invalidArguments);
  const [action, tag, receiptPath] = argv;
  const result = await runLocalImageAction(action, tag, receiptPath);
  if (result) process.stdout.write(JSON.stringify(result) + '\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await main(process.argv.slice(2)); }
  catch (error) {
    const code = Object.values(messages).includes(error?.message) ? error.message : messages.imageCleanup;
    process.stderr.write(code + '\n');
    process.exitCode = 1;
  }
}
