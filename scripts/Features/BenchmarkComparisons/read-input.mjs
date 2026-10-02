import { lstat, readFile, readdir } from 'node:fs/promises';
import { isAbsolute, join, relative, resolve, sep } from 'node:path';
import { artifactLayout, cliOption, cliSyntax, dataEncoding, fileSystemSignal, legacyAttempt, legacyProfiles, legacyRunId, legacySourceSha, mainRef, outputFile, repositoryName, runConclusion, schema3Profiles, triggerEvent, workflowName } from './contracts.mjs';
import { messages } from './messages.mjs';

const allowedEvents = Object.freeze([triggerEvent.push, triggerEvent.workflowDispatch]);
const traversalSegments = new Set([artifactLayout.parentPathSegment, artifactLayout.currentPathSegment]);
const legacyFlag = cliOption.legacyBaseline;
const allowedArguments = new Set(Object.values(cliOption).filter(option => option !== legacyFlag));

function parseArguments(args) {
  const values = new Map();
  for (let index = 0; index < args.length; index++) {
    const argument = args[index];
    if (argument === legacyFlag) {
      if (values.has(legacyFlag)) throw new Error(messages.errors.invalidArguments);
      values.set(legacyFlag, true);
      continue;
    }
    if (!allowedArguments.has(argument) || index + 1 >= args.length || args[index + 1].startsWith(cliSyntax.optionPrefix)) throw new Error(messages.errors.invalidArguments);
    if (values.has(argument)) throw new Error(messages.errors.invalidArguments);
    values.set(argument, args[++index]);
  }
  const required = Object.values(cliOption).filter(option => option !== legacyFlag);
  if (required.some(key => !values.has(key))) throw new Error(messages.errors.invalidArguments);
  const sourceSha = String(values.get(cliOption.sourceSha));
  const runId = Number(values.get(cliOption.runId));
  const attempt = Number(values.get(cliOption.attempt));
  const legacy = values.has(legacyFlag);
  return {
    sourceSha: sourceSha.toLowerCase(),
    runId,
    attempt,
    repository: String(values.get(cliOption.repository)),
    conclusion: String(values.get(cliOption.conclusion)),
    event: String(values.get(cliOption.event)),
    ref: String(values.get(cliOption.ref)),
    workflow: String(values.get(cliOption.workflow)),
    input: String(values.get(cliOption.input)),
    output: String(values.get(cliOption.output)),
    legacy,
  };
}

function validateMetadata(metadata, sourceShaPattern) {
  const valid = sourceShaPattern.test(metadata.sourceSha) && Number.isSafeInteger(metadata.runId) && metadata.runId > 0
    && Number.isInteger(metadata.attempt) && metadata.attempt > 0 && metadata.repository === repositoryName
    && metadata.conclusion === runConclusion.success && allowedEvents.includes(metadata.event) && metadata.ref === mainRef
    && metadata.workflow === workflowName;
  if (!valid) throw new Error(messages.errors.invalidMetadata);
  if (metadata.legacy && (metadata.sourceSha.toLowerCase() !== legacySourceSha || metadata.runId !== legacyRunId
    || metadata.attempt !== legacyAttempt || metadata.event !== triggerEvent.push)) throw new Error(messages.errors.legacyContract);
}

function safePathUnder(parentPath, candidatePath) {
  const relativePath = relative(parentPath, candidatePath);
  return relativePath.length > 0 && !relativePath.startsWith(`${artifactLayout.parentPathSegment}${sep}`)
    && relativePath !== artifactLayout.parentPathSegment && !isAbsolute(relativePath);
}

function containsTraversal(value) {
  return String(value).split(/[\\/]/).some(part => traversalSegments.has(part));
}

async function rejectSymlinkComponents(pathValue) {
  const absolutePath = resolve(pathValue);
  const root = sep;
  let current = root;
  for (const part of absolutePath.slice(root.length).split(sep).filter(Boolean)) {
    current = join(current, part);
    try {
      if ((await lstat(current)).isSymbolicLink()) throw new Error(messages.errors.unsafeInput);
    } catch (error) {
      if (error.code === fileSystemSignal.notFound) break;
      throw error;
    }
  }
  return absolutePath;
}

async function resolveInputPath(repositoryRoot, inputArgument) {
  if (containsTraversal(inputArgument)) throw new Error(messages.errors.unsafeInput);
  const artifactsRoot = join(repositoryRoot, artifactLayout.artifactsDirectory);
  const candidate = await rejectSymlinkComponents(isAbsolute(inputArgument) ? inputArgument : join(repositoryRoot, inputArgument));
  if (!safePathUnder(artifactsRoot, candidate)) throw new Error(messages.errors.unsafeInput);
  const metadata = await lstat(candidate);
  if (!metadata.isDirectory()) throw new Error(messages.errors.unsafeInput);
  return candidate;
}

async function resolveOutputPath(repositoryRoot, outputArgument) {
  if (containsTraversal(outputArgument)) throw new Error(messages.errors.unsafeOutput);
  const candidate = await rejectSymlinkComponents(isAbsolute(outputArgument) ? outputArgument : join(repositoryRoot, outputArgument));
  const allowedRoots = [join(repositoryRoot, artifactLayout.artifactsDirectory), artifactLayout.temporaryRoot];
  if (!allowedRoots.some(root => safePathUnder(root, candidate)) || candidate === resolve(repositoryRoot)) throw new Error(messages.errors.unsafeOutput);
  return candidate;
}

async function profileFiles(inputRoot, legacy) {
  const allowedProfiles = Object.keys(legacy ? legacyProfiles : schema3Profiles);
  const entries = await readdir(inputRoot, { withFileTypes: true });
  const folders = entries.filter(entry => entry.isDirectory()).map(entry => entry.name).sort();
  if (folders.length !== allowedProfiles.length || folders.some(folder => !allowedProfiles.includes(folder))) throw new Error(messages.errors.inputProfiles);
  return Promise.all(folders.map(async profile => {
    const reportPath = join(inputRoot, profile, outputFile.results);
    const reportInfo = await lstat(reportPath).catch(() => null);
    if (!reportInfo?.isFile() || reportInfo.isSymbolicLink()) throw new Error(messages.errors.inputProfiles);
    const raw = await readFile(reportPath);
    let report;
    try { report = JSON.parse(raw.toString(dataEncoding.utf8)); }
    catch { throw new Error(messages.errors.malformedJson(reportPath)); }
    return { profile, report, raw, path: reportPath };
  }));
}

export async function readEvidence(args, repositoryRoot, sourceShaPattern) {
  const metadata = parseArguments(args);
  validateMetadata(metadata, sourceShaPattern);
  const inputRoot = await resolveInputPath(repositoryRoot, metadata.input);
  const outputRoot = await resolveOutputPath(repositoryRoot, metadata.output);
  if (inputRoot === outputRoot || safePathUnder(inputRoot, outputRoot) || safePathUnder(outputRoot, inputRoot)) throw new Error(messages.errors.unsafeOutput);
  return { metadata, outputRoot, inputs: await profileFiles(inputRoot, metadata.legacy) };
}
