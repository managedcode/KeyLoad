import { createHash } from 'node:crypto';
import { imageKind, imageReference, message, outputFormat, processLimit, registry, registryProtocol, validation } from './image-contracts.mjs';
import { recordRegistryHeaders } from './image-evidence.mjs';

const successfulHttpStatus = 200;
const manifestSchemaVersion = 2;
const metadataSeparator = '|';
const mediaTypeParameterSeparator = ';';
const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

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
    try {
      const response = await fetch(`${registry.url}${registryProtocol.path}`, {
        signal: AbortSignal.timeout(processLimit.inspectTimeoutMs),
        redirect: 'error',
      });
      await response.body?.cancel();
      if (response.status === successfulHttpStatus) return;
    } catch {
    }
    await wait(processLimit.readinessIntervalMs);
  }
  throw new Error(message.registryTimeout);
}

export async function fetchManifest(context, imageName, tag, image) {
  const url = `${registry.url}${registryProtocol.repositories}${imageName}${registryProtocol.manifests}${tag}`;
  let response;
  try {
    response = await fetch(url, {
      headers: { [registryProtocol.accept]: registryProtocol.acceptHeader },
      signal: AbortSignal.timeout(processLimit.inspectTimeoutMs),
      redirect: 'error',
    });
  } catch {
    throw new Error(message.fetchFailed);
  }
  const digestHeader = response.headers.get(registryProtocol.digestHeader);
  const contentTypeHeader = response.headers.get(registryProtocol.contentTypeHeader) ?? '';
  const contentLengthHeader = response.headers.get(registryProtocol.contentLengthHeader);
  await recordRegistryHeaders(context, imageName, response.status, {
    contentType: contentTypeHeader,
    digest: digestHeader,
    contentLength: contentLengthHeader,
  });
  if (response.status !== successfulHttpStatus) {
    await response.body?.cancel();
    throw new Error(message.fetchFailed);
  }
  const contentLength = Number(contentLengthHeader);
  if (Number.isFinite(contentLength) && contentLength > processLimit.maxManifestBytes) {
    await response.body?.cancel();
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
  const chunks = [];
  let byteCount = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      byteCount += value.byteLength;
      if (byteCount > processLimit.maxManifestBytes) {
        await reader.cancel();
        throw new Error(message.invalidManifest);
      }
      chunks.push(Buffer.from(value));
    }
  } finally {
    reader.releaseLock();
  }
  return Buffer.concat(chunks, byteCount);
}

export function imageOutputName(kind) {
  return kind === imageKind.server ? imageReference.outputServer : imageReference.outputLoadGenerator;
}
