import path from 'node:path';
import { directoryName, envName, message, operatingSystem, validation } from './native-process-contracts.mjs';

const emptyValue = '';

export function createRunContext(environment, platform) {
  const sourceSha = required(environment, envName.githubSha);
  const runId = required(environment, envName.runId);
  const runAttempt = required(environment, envName.runAttempt);
  const repository = required(environment, envName.repository);
  const ref = required(environment, envName.ref);
  const runnerTemp = required(environment, envName.runnerTemp);
  const workspace = required(environment, envName.workspace);
  const githubOutput = required(environment, envName.githubOutput);

  if (!validation.shaPattern.test(sourceSha)
    || !validation.numericIdPattern.test(runId)
    || !validation.numericIdPattern.test(runAttempt)
    || !validation.repositoryPattern.test(repository)
    || repository.length > validation.maxRepositoryLength
    || !validation.refPattern.test(ref)
    || ref.length > validation.maxRefLength
    || !path.isAbsolute(runnerTemp)
    || !path.isAbsolute(workspace)
    || !path.isAbsolute(githubOutput)
    || runnerTemp.length > validation.maxPathLength
    || workspace.length > validation.maxPathLength
    || githubOutput.length > validation.maxPathLength) {
    throw new Error(message.invalidEnvironment);
  }
  if (platform !== operatingSystem.linux) throw new Error(message.unsupportedPlatform);

  const evidenceDirectory = path.resolve(runnerTemp, directoryName.evidence);
  const resolvedTemp = path.resolve(runnerTemp);
  if (path.dirname(evidenceDirectory) !== resolvedTemp || resolvedTemp === path.parse(resolvedTemp).root) {
    throw new Error(message.invalidWorkspace);
  }

  return Object.freeze({
    sourceSha: sourceSha.toLowerCase(),
    runId,
    runAttempt,
    repository,
    ref,
    runnerTemp: resolvedTemp,
    workspace: path.resolve(workspace),
    githubOutput: path.resolve(githubOutput),
    evidenceDirectory,
  });
}

export function assertCleanSourceRevision(context, headOutput, diffExitCode, statusOutput) {
  const head = headOutput.trim().toLowerCase();
  if (head !== context.sourceSha) throw new Error(message.sourceMismatch);
  if (diffExitCode !== 0 || statusOutput.trim() !== emptyValue) throw new Error(message.trackedChanges);
}

function required(environment, name) {
  const value = environment?.[name];
  if (typeof value !== 'string' || value.length === 0 || value.trim() !== value || validation.controlCharacterPattern.test(value)) {
    throw new Error(message.invalidEnvironment);
  }
  return value;
}
