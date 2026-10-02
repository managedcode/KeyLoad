import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { dockerArgument, exitCode, message, outputFormat, processLimit, receiptField, registry, safeErrorMessage, validation } from './image-contracts.mjs';
import { createRunContext, validateEntryArguments } from './image-inputs.mjs';
import { ensureEvidenceDirectory, writeCleanupCapture, writeCleanupSummary } from './image-evidence.mjs';
import { removeOwnedRegistry, runDocker, verifyDockerEngine } from './image-engine.mjs';

const emptyBuffer = Buffer.alloc(0);
const noSuchObject = 'No such object:';
const registryPortKey = '5000/tcp';
const loopbackAddress = '127.0.0.1';

export async function cleanupImages(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createRunContext(environment, process.platform);
  await ensureEvidenceDirectory(context);
  await verifyDockerEngine(context);

  const inspected = await inspectOwnedName(context);
  if (!inspected.found) {
    await writeCleanupCapture(context, emptyBuffer, Buffer.from(JSON.stringify({ found: false }), outputFormat.utf8));
    await writeCleanupSummary(context, {
      [receiptField.cleanup]: Object.freeze({
        [receiptField.containerName]: context.containerName,
        [receiptField.owned]: false,
        [receiptField.found]: false,
        [receiptField.removed]: false,
        [receiptField.exitCode]: null,
        [receiptField.result]: 'success',
      }),
    });
    return exitCode.success;
  }

  assertRegistryOwnership(context, inspected.container);
  const logResult = await runDocker(context, [dockerArgument.logs, dockerArgument.tail, `${processLimit.maxLogLines}`, context.containerName], {
    operation: 'capture-registry-logs',
    timeoutMs: processLimit.cleanupTimeoutMs,
    maximumOutputBytes: processLimit.maxLogBytes,
    truncateOutput: true,
  }).catch(() => null);
  const state = summarizeRegistryState(inspected.container);
  const logBytes = logResult ? Buffer.concat([Buffer.from(logResult.stdout, outputFormat.utf8), Buffer.from(logResult.stderr, outputFormat.utf8)]) : emptyBuffer;
  const stateBytes = Buffer.from(`${JSON.stringify(state)}${outputFormat.newline}`, outputFormat.utf8);
  await writeCleanupCapture(context, logBytes, stateBytes);
  const removed = await removeIfStillOwned(context, inspected.container).catch(() => false);
  const succeeded = Boolean(logResult && logResult.code === 0 && removed);
  const record = {
    [receiptField.cleanup]: Object.freeze({
      [receiptField.containerName]: context.containerName,
      [receiptField.owned]: true,
      [receiptField.found]: true,
      [receiptField.removed]: removed,
      [receiptField.exitCode]: state.exitCode,
      [receiptField.result]: succeeded ? 'success' : 'failure',
      [receiptField.outputTruncated]: logResult?.outputTruncated ?? false,
    }),
  };
  await writeCleanupSummary(context, record);
  if (!succeeded) throw new Error(message.cleanupFailed);
  return exitCode.success;
}

async function removeIfStillOwned(context, originalContainer) {
  const latest = await inspectOwnedName(context);
  if (!latest.found) return true;
  assertRegistryOwnership(context, latest.container);
  if (latest.container.Id !== originalContainer.Id) throw new Error(message.foreignRegistry);
  return removeOwnedRegistry(context, latest.container.Id);
}

async function inspectOwnedName(context) {
  const result = await runDocker(context, [dockerArgument.inspect, dockerArgument.format, dockerArgument.inspectTemplate, context.containerName], {
    operation: 'inspect-registry-owner',
    captureOutput: false,
    timeoutMs: processLimit.inspectTimeoutMs,
    maximumOutputBytes: processLimit.maxInspectBytes,
  });
  if (result.code !== 0) {
    if (result.stderr.includes(noSuchObject)) return Object.freeze({ found: false, container: null });
    throw new Error(message.commandFailed);
  }
  let parsed;
  try {
    parsed = JSON.parse(result.stdout);
  } catch {
    throw new Error(message.invalidOutput);
  }
  const container = Array.isArray(parsed) ? parsed[0] : parsed;
  if (!container || typeof container !== 'object') throw new Error(message.invalidOutput);
  return Object.freeze({ found: true, container });
}

function assertRegistryOwnership(context, container) {
  const labels = container?.Config?.Labels;
  const containerName = container?.Name;
  const ports = container?.HostConfig?.PortBindings?.[registryPortKey];
  const port = Array.isArray(ports) ? ports[0] : null;
  if (containerName !== `/${context.containerName}`
    || !validation.containerIdPattern.test(container?.Id ?? '')
    || container?.Config?.Image !== registry.image
    || labels?.[registry.ownerRunLabel] !== context.ownerLabels.runId
    || labels?.[registry.ownerAttemptLabel] !== context.ownerLabels.runAttempt
    || labels?.[registry.ownerRepositoryLabel] !== context.ownerLabels.repository
    || port?.HostIp !== loopbackAddress
    || port?.HostPort !== `${registry.port}`) {
    throw new Error(message.foreignRegistry);
  }
}

function summarizeRegistryState(container) {
  const state = container?.State;
  return Object.freeze({
    name: container?.Name,
    image: container?.Config?.Image,
    status: state?.Status,
    running: Boolean(state?.Running),
    exitCode: !state?.Running && Number.isInteger(state?.ExitCode) ? state.ExitCode : null,
    startedAt: state?.StartedAt,
    finishedAt: state?.FinishedAt,
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    process.exitCode = await cleanupImages();
  } catch (error) {
    process.stderr.write(`${safeErrorMessage(error, message.cleanupFailed)}${outputFormat.newline}`);
    process.exitCode = exitCode.failure;
  }
}
