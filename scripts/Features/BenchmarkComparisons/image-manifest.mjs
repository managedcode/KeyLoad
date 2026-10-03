import { createHash } from 'node:crypto';
import { imageKind, imageReference, message, outputFormat, processLimit, registry, registryProtocol, safeErrorMessage, validation } from './image-contracts.mjs';
import { recordRegistryHeaders } from './image-evidence.mjs';

const successfulHttpStatus = 200;
const manifestSchemaVersion = 2;
const metadataSeparator = '|';
const mediaTypeParameterSeparator = ';';
const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const invalidDeadline = 'The owned image HTTP deadline is invalid.';
const timeoutMessage = 'The owned image HTTP operation exceeded its time bound.';
const cleanupErrorGroups = new WeakMap();

export async function withHttpDeadline(timeoutMs, operation) {
  if (!Number.isInteger(timeoutMs) || timeoutMs <= 0 || timeoutMs > processLimit.inspectTimeoutMs) {
    throw new RangeError(invalidDeadline);
  }
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(new DOMException(timeoutMessage, 'TimeoutError')), timeoutMs);
  try {
    return await operation(controller.signal);
  } finally {
    clearTimeout(timer);
  }
}

export function parseImageMetadata(output, expectedRevision) {
  if (typeof output !== 'string' || typeof expectedRevision !== 'string' || !validation.shaPattern.test(expectedRevision)) {
    throw new Error(message.invalidOutput);
  }
  const values = output.trim().split(metadataSeparator);
  const [configImageId, sourceRevision] = values;
  if (values.length !== 2 || !validation.configIdPattern.test(configImageId ?? '') || sourceRevision?.toLowerCase() !== expectedRevision.toLowerCase()) {
    throw new Error(message.invalidOutput);
  }
  return Object.freeze({ configImageId: configImageId.toLowerCase(), sourceRevision: sourceRevision.toLowerCase() });
}

export function parseManifestEvidence(bytes, digestHeader, contentTypeHeader, expectedRevision, configImageId, imageName, taggedReference) {
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > processLimit.maxManifestBytes
    || !validation.digestPattern.test(digestHeader ?? '')
    || !validation.configIdPattern.test(configImageId ?? '')
    || ![imageReference.serverName, imageReference.comparisonsName].includes(imageName)
    || typeof expectedRevision !== 'string'
    || !validation.shaPattern.test(expectedRevision)
    || typeof contentTypeHeader !== 'string') {
    throw new Error(message.invalidManifest);
  }
  const contentType = contentTypeHeader.split(mediaTypeParameterSeparator)[0].trim().toLowerCase();
  if (![registryProtocol.ociManifestMediaType, registryProtocol.dockerManifestMediaType].includes(contentType)) {
    throw new Error(message.invalidManifest);
  }
  const manifestDigest = `sha256:${createHash(outputFormat.sha256).update(bytes).digest(outputFormat.hex)}`;
  if (manifestDigest.toLowerCase() !== digestHeader.toLowerCase()
    || configImageId.toLowerCase() === manifestDigest.toLowerCase()) throw new Error(message.invalidManifest);

  let document;
  try {
    document = JSON.parse(bytes.toString(outputFormat.utf8));
  } catch {
    throw new Error(message.invalidManifest);
  }
  const configDigest = document?.config?.digest;
  const manifestMediaType = document?.mediaType;
  if (document?.schemaVersion !== manifestSchemaVersion
    || typeof configDigest !== 'string'
    || configDigest.toLowerCase() !== configImageId.toLowerCase()
    || (manifestMediaType !== undefined && (typeof manifestMediaType !== 'string' || contentType !== manifestMediaType.toLowerCase()))) {
    throw new Error(message.invalidManifest);
  }

  const finalReference = `${taggedReference}${imageReference.digestSeparator}${manifestDigest}`;
  if (!validation.imageReferencePattern.test(finalReference)) throw new Error(message.invalidManifest);
  const kind = imageName === imageKind.server ? imageKind.server : imageKind.comparisons;
  return Object.freeze({
    name: kind,
    finalReference,
    manifestBytes: bytes,
    manifest: Object.freeze({
      manifestBytesBase64: bytes.toString(outputFormat.base64),
      manifestSha256: manifestDigest,
      digestHeader: digestHeader.toLowerCase(),
      contentTypeHeader,
    }),
    configImageId: configImageId.toLowerCase(),
    sourceLabel: registry.sourceLabel,
    sourceLabelValue: expectedRevision.toLowerCase(),
  });
}

export async function waitForRegistry(context) {
  const deadline = Date.now() + processLimit.readinessTimeoutMs;
  while (Date.now() < deadline) {
    const remaining = deadline - Date.now();
    if (remaining <= 0) break;
    try {
      const result = await withHttpDeadline(Math.min(processLimit.inspectTimeoutMs, remaining), async signal => {
        const response = await fetch(`${registry.url}${registryProtocol.path}`, { signal, redirect: 'error' });
        await response.body?.cancel();
        return { status: response.status, aborted: signal.aborted };
      });
      if (result.status === successfulHttpStatus && !result.aborted && Date.now() < deadline) return;
    } catch {
    }
    const backoff = Math.min(processLimit.readinessIntervalMs, deadline - Date.now());
    if (backoff > 0) await wait(backoff);
  }
  throw new Error(message.registryTimeout);
}

export async function fetchManifest(context, imageName, tag, image) {
  const url = `${registry.url}${registryProtocol.repositories}${imageName}${registryProtocol.manifests}${tag}`;
  return withHttpDeadline(processLimit.inspectTimeoutMs, signal =>
    fetchManifestWithSignal(context, imageName, image, url, signal));
}

async function fetchManifestWithSignal(context, imageName, image, url, signal) {
  let response;
  try {
    response = await fetch(url, {
      headers: { [registryProtocol.accept]: registryProtocol.acceptHeader },
      signal,
      redirect: 'error',
    });
  } catch {
    throw new Error(message.fetchFailed);
  }
  return readManifestAndClose(context, imageName, image, response);
}

async function readManifestAndClose(context, imageName, image, response) {
  let failed = false;
  let failure;
  try {
    return await readManifestContents(context, imageName, image, response);
  } catch (error) {
    failed = true;
    failure = error;
    throw error;
  } finally {
    try {
      await response.body?.cancel();
    } catch (cleanup) {
      if (failed) throw combineCleanupErrors([failure, cleanup]);
      throw cleanup;
    }
  }
}

async function readManifestContents(context, imageName, image, response) {
  const digestHeader = response.headers.get(registryProtocol.digestHeader);
  const contentTypeHeader = response.headers.get(registryProtocol.contentTypeHeader) ?? '';
  const contentLengthHeader = response.headers.get(registryProtocol.contentLengthHeader);
  await recordRegistryHeaders(context, imageName, response.status, {
    contentType: contentTypeHeader,
    digest: digestHeader,
    contentLength: contentLengthHeader,
  });
  if (response.status !== successfulHttpStatus) {
    throw new Error(message.fetchFailed);
  }
  const contentLength = Number(contentLengthHeader);
  if (Number.isFinite(contentLength) && contentLength > processLimit.maxManifestBytes) {
    throw new Error(message.invalidManifest);
  }
  const bytes = await readBoundedBody(response);
  return parseManifestEvidence(
    bytes,
    digestHeader,
    contentTypeHeader,
    image.sourceRevision,
    image.configImageId,
    imageName,
    image.taggedReference,
  );
}

async function readBoundedBody(response) {
  const reader = response.body?.getReader();
  if (!reader) throw new Error(message.invalidManifest);
  const failures = [];
  try {
    return await readManifestBytes(reader);
  } catch (error) {
    failures.push(error);
    throw error;
  } finally {
    await closeManifestReader(reader, failures);
  }
}

async function readManifestBytes(reader) {
  const chunks = [];
  let byteCount = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    byteCount += value.byteLength;
    if (byteCount > processLimit.maxManifestBytes) throw new Error(message.invalidManifest);
    chunks.push(Buffer.from(value));
  }
  return Buffer.concat(chunks, byteCount);
}

async function closeManifestReader(reader, failures) {
  try {
    await reader.cancel();
  } catch (error) {
    failures.push(error);
  }
  try {
    reader.releaseLock();
  } catch (error) {
    failures.push(error);
  }
  if (failures.length > 0) throw combineCleanupErrors(failures);
}

function combineCleanupErrors(failures) {
  const distinct = [...new Set(failures)];
  if (distinct.length === 1) return distinct[0];
  const unique = [...new Set(distinct.flatMap(error => cleanupErrorGroups.get(error) ?? [error]))];
  if (unique.length === 1) return unique[0];
  const combined = new AggregateError(unique, safeErrorMessage(failures[0], message.cleanupFailed));
  cleanupErrorGroups.set(combined, Object.freeze(unique));
  return combined;
}

export function imageOutputName(kind) {
  return kind === imageKind.server ? imageReference.outputServer : imageReference.outputLoadGenerator;
}
