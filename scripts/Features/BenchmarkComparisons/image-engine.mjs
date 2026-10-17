import { readFile } from 'node:fs/promises';
import path from 'node:path';
import {
  baseImage, buildArgument, dockerArgument, envName, imageKind, imageReference, message, operatingSystem,
  processLimit, registry, registryProtocol, validation,
} from './image-contracts.mjs';
import { assertLocalUnixEndpoint } from './image-inputs.mjs';
import { parseImageMetadata } from './image-manifest.mjs';
import { recordNativeCommand } from './image-evidence.mjs';
import { requireSuccessful, runBounded } from './image-process.mjs';

const emptyValue = '';
const linuxPlatform = 'linux';
const dockerCommand = dockerArgument.docker;
const containerSettingsPath = 'Directory.Build.props';
const productProjects = Object.freeze({
  [imageKind.server]: 'src/KeyLoad.Server/KeyLoad.Server.csproj',
  [imageKind.comparisons]: 'benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj',
});

export async function verifyContainerPins(workspace, kinds = [imageKind.server, imageKind.comparisons]) {
  const source = await readFile(path.join(workspace, containerSettingsPath), 'utf8');
  const sdk = JSON.parse(await readFile(path.join(workspace, 'global.json'), 'utf8')).sdk?.version;
  const pins = [...source.matchAll(/<ContainerBaseImage>([^<]+)<\/ContainerBaseImage>/gu)];
  if (pins.length !== 1 || pins[0][1] !== baseImage.aspnet
    || `dotnet-sdk:${sdk}` !== baseImage.sdk || kinds.some(kind => !productProjects[kind])) {
    throw new Error(message.invalidWorkspace);
  }
  for (const kind of kinds) {
    const project = await readFile(path.join(workspace, productProjects[kind]), 'utf8').catch(() => emptyValue);
    if (!project.includes('<Project')) throw new Error(message.invalidWorkspace);
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
  await verifyContainerPins(source.workspace, [kind]);
  const sdk = await requireSuccessful('dotnet', ['--version'], {
    cwd: source.workspace, timeoutMs: processLimit.inspectTimeoutMs,
    recordResult: result => recordNativeCommand(context, 'sdk-version', result),
  });
  if (`dotnet-sdk:${sdk.trim()}` !== baseImage.sdk) throw new Error(message.invalidWorkspace);
  const tag = taggedReference.slice(taggedReference.lastIndexOf(':') + 1);
  const repository = kind === imageKind.server ? 'keyload/server' : 'keyload/comparisons';
  const localReference = `${repository}:${tag}`;
  await requireSuccessful('dotnet', [
    'publish', productProjects[kind], '/t:PublishContainer', '--disable-build-servers', '--configuration', 'Release',
    '--runtime', 'linux-x64', '-warnaserror', `-p:ContainerRepository=${repository}`,
    `-p:ContainerImageTag=${tag}`, `-p:SourceRevisionId=${source.sourceSha}`,
  ], { cwd: source.workspace, timeoutMs: processLimit.buildTimeoutMs,
    recordResult: result => recordNativeCommand(context, kind === imageKind.server ? 'build-server' : 'build-comparisons', result),
  });
  await requireDockerSuccess(context, 'tag-product-image', ['tag', localReference, taggedReference], {
    cwd: source.workspace, timeoutMs: processLimit.inspectTimeoutMs,
  });
  await requireDockerSuccess(context, 'remove-product-build-alias', ['image', 'rm', localReference], {
    cwd: source.workspace, timeoutMs: processLimit.inspectTimeoutMs,
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
