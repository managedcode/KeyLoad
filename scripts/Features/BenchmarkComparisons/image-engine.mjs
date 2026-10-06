import { readFile } from 'node:fs/promises';
import path from 'node:path';
import {
  baseImage, buildArgument, dockerArgument, envName, imageBuild, imageKind, imageReference, message, operatingSystem,
  processLimit, registry, registryProtocol, validation,
} from './image-contracts.mjs';
import { assertLocalUnixEndpoint } from './image-inputs.mjs';
import { parseImageMetadata } from './image-manifest.mjs';
import { recordNativeCommand } from './image-evidence.mjs';
import { requireSuccessful, runBounded } from './image-process.mjs';

const emptyValue = '';
const linuxPlatform = 'linux';
const dockerCommand = dockerArgument.docker;
const expectedSchemaPins = Object.freeze([baseImage.sdk, baseImage.aspnet]);

export async function verifyDockerfilePins(workspace, kinds = [imageKind.server, imageKind.comparisons]) {
  const dockerfiles = kinds.map(kind => kind === imageKind.server
    ? imageBuild.serverDockerfile : imageBuild.runnerDockerfile);
  for (const relativePath of dockerfiles) {
    const source = await readFile(path.join(workspace, relativePath), 'utf8').catch(() => emptyValue);
    if (expectedSchemaPins.some(pin => !source.includes(pin))) throw new Error(message.invalidWorkspace);
  }
}

export async function verifyDockerEngine(context) {
  if (process.platform !== operatingSystem.linux) throw new Error(message.unsupportedPlatform);
  const activeContext = (await requireDockerSuccess(context, 'docker-context-show', [dockerArgument.context, dockerArgument.show], {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
    captureOutput: false,
  })).trim();
  if (!activeContext || /[\r\n]/.test(activeContext)) throw new Error(message.remoteEngine);
  const endpoint = (await requireDockerSuccess(context, 'docker-context-inspect', [dockerArgument.context, dockerArgument.contextInspect, activeContext, dockerArgument.format, dockerArgument.contextEndpointTemplate], {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
    captureOutput: false,
  })).trim();
  assertLocalUnixEndpoint(endpoint, process.env[envName.dockerHost] ?? emptyValue);
  const engineOperatingSystem = (await requireDockerSuccess(context, 'docker-info-inspect', [dockerArgument.info, dockerArgument.format, dockerArgument.operatingSystemTemplate], {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
    captureOutput: false,
  })).trim();
  if (engineOperatingSystem !== linuxPlatform) throw new Error(message.unsupportedPlatform);
  await requireDockerSuccess(context, 'docker-version', [dockerArgument.version], { cwd: context.workspace, timeoutMs: processLimit.inspectTimeoutMs });
}

export async function verifyBuildx(context) {
  await requireDockerSuccess(context, 'buildx-version', [buildArgument.buildx, buildArgument.version], {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
  const details = await requireDockerSuccess(context, 'buildx-inspect', [buildArgument.buildx, buildArgument.inspect], {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
  if (!details.includes(imageBuild.platform)) throw new Error(message.missingBuildx);
}

export function makeImageTag(context) {
  return [context.sourceSha, context.runId, context.runAttempt].join(imageReference.runSeparator);
}

export function makeTaggedReference(kind, tag) {
  const name = kind === imageKind.server ? imageReference.serverName : imageReference.comparisonsName;
  return `${imageReference.repositoryPrefix}${name}${imageReference.tagSeparator}${tag}`;
}

export async function buildProductImage(context, kind, taggedReference, imageSource = undefined) {
  const source = imageSource ?? context;
  if (!path.isAbsolute(source.workspace ?? '')
    || source.workspace.length > validation.maxPathLength
    || !validation.shaPattern.test(source.sourceSha ?? '')) throw new Error(message.invalidWorkspace);
  const dockerfile = kind === imageKind.server ? imageBuild.serverDockerfile : imageBuild.runnerDockerfile;
  const args = [
    buildArgument.buildx,
    buildArgument.build,
    buildArgument.platform,
    imageBuild.platform,
    buildArgument.load,
    buildArgument.tag,
    taggedReference,
    buildArgument.file,
    dockerfile,
    buildArgument.label,
    `${registry.sourceLabel}=${source.sourceSha}`,
    imageBuild.context,
  ];
  await requireDockerSuccess(context, kind === imageKind.server ? 'build-server' : 'build-comparisons', args, {
    cwd: source.workspace,
    timeoutMs: processLimit.buildTimeoutMs,
  });
  const metadata = await requireDockerSuccess(context, kind === imageKind.server ? 'inspect-server-image' : 'inspect-comparisons-image', [
    dockerArgument.image,
    dockerArgument.inspect,
    buildArgument.format,
    buildArgument.configLabelTemplate,
    taggedReference,
  ], {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
  const imageMetadata = parseImageMetadata(metadata, source.sourceSha);
  return Object.freeze({ taggedReference, ...imageMetadata });
}

export async function pushProductImage(context, taggedReference) {
  await requireDockerSuccess(context, taggedReference.includes('/server:') ? 'push-server' : 'push-comparisons', [buildArgument.push, taggedReference], {
    cwd: context.workspace,
    timeoutMs: processLimit.pushTimeoutMs,
  });
}

export async function startOwnedRegistry(context) {
  const labels = context.ownerLabels;
  const args = [
    dockerArgument.run,
    dockerArgument.detach,
    dockerArgument.name,
    context.containerName,
    dockerArgument.publish,
    `${registry.host}:${registry.port}:${registry.port}`,
    dockerArgument.environment,
    registryProtocol.registryDeleteDisabled,
    dockerArgument.label,
    `${registry.ownerRunLabel}=${labels.runId}`,
    dockerArgument.label,
    `${registry.ownerAttemptLabel}=${labels.runAttempt}`,
    dockerArgument.label,
    `${registry.ownerRepositoryLabel}=${labels.repository}`,
    registry.image,
  ];
  await requireDockerSuccess(context, 'start-owned-registry', args, {
    cwd: context.workspace,
    timeoutMs: processLimit.inspectTimeoutMs,
  });
}

export async function removeOwnedRegistry(context, containerId) {
  if (!validation.containerIdPattern.test(containerId ?? '')) throw new Error(message.foreignRegistry);
  const result = await runBounded(dockerCommand, [dockerArgument.remove, dockerArgument.force, containerId], {
    cwd: context.workspace,
    timeoutMs: processLimit.cleanupTimeoutMs,
  });
  await recordNativeCommand(context, 'remove-owned-registry', result);
  return result.code === 0;
}

export async function runDocker(context, argumentsList, options = {}) {
  const result = await runBounded(dockerCommand, argumentsList, {
    cwd: context.workspace,
    timeoutMs: options.timeoutMs ?? processLimit.inspectTimeoutMs,
    maximumOutputBytes: options.maximumOutputBytes,
    truncateOutput: options.truncateOutput === true,
  });
  await recordNativeCommand(context, options.operation ?? 'docker-inspect', result, options.captureOutput !== false);
  return result;
}

async function requireDockerSuccess(context, operation, argumentsList, options = {}) {
  return requireSuccessful(dockerCommand, argumentsList, {
    ...options,
    recordResult: result => recordNativeCommand(context, operation, result, options.captureOutput !== false),
  });
}
