import { createHash } from 'node:crypto';
import { lstat, readFile, readdir } from 'node:fs/promises';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const SCRIPT_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_ROOT = resolve(SCRIPT_DIRECTORY, '../../..');
const RECORD_PATH = 'docs/implementation/mcaf-installation.json';
const ROOT_POLICY_PATH = 'AGENTS.md';
const RECORD_ARRAY_FIELDS = Object.freeze({ projects: 'projects', modules: 'modules', skills: 'skillFiles' });
const PATH_SEPARATOR = '/';
const REQUIRED_FILES = [
  'docs/Features/RepositoryGovernance.md',
  'docs/ADR/ADR-032-mcaf-governance.md',
  'docs/Architecture.md',
];
const WORKING_FILE_SUFFIXES = ['.plan.md', '.brainstorm.md', '.acceptance.md'];
const REQUIRED_POLICY_IDS = [
  'MCAF-GOV-001',
  'MCAF-ARCH-001',
  'MCAF-AI-001',
  'MCAF-REQ-001',
];
const REQUIRED_COMMAND_MARKERS = [
  'dotnet restore KeyLoad.slnx',
  'dotnet build KeyLoad.slnx',
  'dotnet format KeyLoad.slnx',
  'gh workflow run ci.yml',
  'node scripts/Features/RepositoryGovernance/verify.mjs',
];
const SKIPPED_DIRECTORY_NAMES = new Set([
  '.git',
  'bin',
  'obj',
  'artifacts',
  'output',
  'node_modules',
  '.data',
]);
const LOCAL_POLICY_MARKERS = [
  /purpose/i,
  /entry\s+points?/i,
  /boundar(y|ies)|ownership/i,
  /commands?|verification/i,
  /skills?/i,
  /risks?|protected/i,
];
const PLACEHOLDER_PATTERNS = [
  /\bTODO\s*:/i,
  /\bTBD\b/i,
  /lorem ipsum/i,
  /fill\s+(in|this|me)\b/i,
  /your\s+(project|organization|name)\b/i,
  /\{\{[^}]+\}\}/,
];
const namedMessages = {
  usage: 'Usage: node scripts/Features/RepositoryGovernance/verify.mjs [--root <path>]',
  repositoryPath: value => `Repository path must be a safe relative path: ${value}`,
  symbolicLinkPrefix: 'Refusing to follow symbolic link: ',
  symbolicLink: value => `Refusing to follow symbolic link: ${value}`,
  invalidRecord: 'Installation record must be a JSON object.',
  invalidRootRecord: 'preservedRoot must declare AGENTS.md, a positive prefixBytes value and a SHA256 digest.',
  shortRoot: value => `AGENTS.md is shorter than preserved prefix length ${value}.`,
  prefixMismatch: (expected, actual) => `AGENTS.md preserved prefix SHA256 mismatch: expected ${expected}, found ${actual}.`,
  invalidArray: field => `Installation record ${field} must be an array of unique, nonempty, safe relative paths.`,
  invalidArrayItem: (field, value) => `Installation record ${field} contains an invalid path: ${String(value)}`,
  duplicateArrayItem: (field, value) => `Installation record ${field} contains a duplicate path: ${value}`,
  missingItem: (label, value) => `${label} is missing: ${value}`,
  unexpectedItem: (label, value) => `${label} is unexpected: ${value}`,
  missingPolicy: value => `Required local policy is missing: ${value}`,
  incompletePolicy: value => `${value} must document purpose, entry points, boundaries, commands, skills policy and protected risks.`,
  unresolvedPlaceholder: value => `${value} contains an unresolved placeholder.`,
  missingGovernanceFile: value => `Required governance file is missing: ${value}`,
  flatFeatureSource: value => `Feature source requires a responsibility folder: ${value}`,
  workingFile: value => `Working planning file is present: ${value}`,
  missingRootPolicy: value => `Root AGENTS.md is missing required policy ${value}.`,
  missingCommand: value => `Root AGENTS.md is missing the required command: ${value}.`,
  rootPlaceholder: 'Root AGENTS.md contains an unresolved template placeholder.',
  skillsMustBeDisabled: 'Installation record must state skillsInstalled: false.',
  skillDirectories: values => `Repository skill directories are present: ${values.join(', ')}.`,
  failurePrefix: 'FAIL: ',
  failureSummary: count => `Governance validation failed with ${count} issue(s).\n`,
  success: (projects, modules) => `PASS: preserved root prefix, ${projects} projects, ${modules} modules, required governance, local policies and unchanged empty skill inventory.\n`,
};

function parseRoot(argumentsList) {
  if (argumentsList.length === 0) return DEFAULT_ROOT;
  if (argumentsList.length === 2 && argumentsList[0] === '--root') {
    return resolve(argumentsList[1]);
  }
  throw new Error(namedMessages.usage);
}

function toRepositoryPath(root, absolutePath) {
  return relative(root, absolutePath).split(sep).join('/');
}

async function readUtf8(root, repositoryPath) {
  return readFile(await resolveRegularFile(root, repositoryPath), 'utf8');
}

async function resolveRegularFile(root, repositoryPath) {
  if (repositoryPath.startsWith(PATH_SEPARATOR) || repositoryPath.includes('\\') || repositoryPath.split(PATH_SEPARATOR).some(part => part === '..' || part === '.')) {
    throw new Error(namedMessages.repositoryPath(repositoryPath));
  }
  const parts = repositoryPath.split('/');
  let currentPath = root;
  for (const part of parts) {
    currentPath = join(currentPath, part);
    const metadata = await lstat(currentPath);
    if (metadata.isSymbolicLink()) throw new Error(namedMessages.symbolicLink(repositoryPath));
  }
  return currentPath;
}

async function exists(root, repositoryPath) {
  try {
    const metadata = await lstat(await resolveRegularFile(root, repositoryPath));
    return metadata.isFile();
  } catch (error) {
    if (error.code === 'ENOENT' || error.code === 'EISDIR' || error.message.startsWith(namedMessages.symbolicLinkPrefix)) return false;
    throw error;
  }
}

async function walkRepository(root, currentDirectory, inventory) {
  const entries = await readdir(currentDirectory, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isSymbolicLink()) continue;
    const absolutePath = join(currentDirectory, entry.name);
    if (entry.isDirectory()) {
      if (SKIPPED_DIRECTORY_NAMES.has(entry.name)) continue;
      if (entry.name.toLowerCase() === 'skills') inventory.skillDirectories.push(toRepositoryPath(root, absolutePath));
      await walkRepository(root, absolutePath, inventory);
      continue;
    }
    if (!entry.isFile()) continue;
    const repositoryPath = toRepositoryPath(root, absolutePath);
    if (WORKING_FILE_SUFFIXES.some(suffix => entry.name.endsWith(suffix))) inventory.workingFiles.push(repositoryPath);
    if (entry.name.endsWith('.cs') && /(?:^|\/)Features\/[^/]+\/[^/]+\.cs$/.test(repositoryPath)) inventory.flatFeatureSources.push(repositoryPath);
    if (entry.name.endsWith('.csproj')) inventory.projects.push(repositoryPath);
    if (entry.name === 'SKILL.md') inventory.skillFiles.push(repositoryPath);
  }
}

function addMissingItems(diagnostics, label, expected, actual) {
  const actualSet = new Set(actual);
  const expectedSet = new Set(expected);
  for (const item of expected) {
    if (!actualSet.has(item)) diagnostics.push(namedMessages.missingItem(label, item));
  }
  for (const item of actual) {
    if (!expectedSet.has(item)) diagnostics.push(namedMessages.unexpectedItem(label, item));
  }
}

function isSafeRelativePath(value) {
  if (typeof value !== 'string' || value.trim().length === 0 || value.includes('\\') || value.startsWith(PATH_SEPARATOR)) return false;
  return value.split(PATH_SEPARATOR).every(part => part.length > 0 && part !== '.' && part !== '..');
}

function validateRecordPathArray(record, field, diagnostics) {
  const values = record[field];
  if (!Array.isArray(values)) {
    diagnostics.push(namedMessages.invalidArray(field));
    return [];
  }
  const uniqueValues = [];
  const seenValues = new Set();
  for (const value of values) {
    if (!isSafeRelativePath(value)) {
      diagnostics.push(namedMessages.invalidArrayItem(field, value));
    } else if (seenValues.has(value)) {
      diagnostics.push(namedMessages.duplicateArrayItem(field, value));
    } else {
      seenValues.add(value);
      uniqueValues.push(value);
    }
  }
  return uniqueValues;
}

function validateRecordCollections(record, diagnostics) {
  return {
    projects: validateRecordPathArray(record, RECORD_ARRAY_FIELDS.projects, diagnostics),
    modules: validateRecordPathArray(record, RECORD_ARRAY_FIELDS.modules, diagnostics),
    skillFiles: validateRecordPathArray(record, RECORD_ARRAY_FIELDS.skills, diagnostics),
  };
}

async function validateRecordAndInventory(root, record, inventory, diagnostics) {
  const collections = validateRecordCollections(record, diagnostics);
  await validatePreservedPrefix(root, record, diagnostics);
  addMissingItems(diagnostics, 'Project inventory', [...collections.projects].sort(), inventory.projects);
  addMissingItems(diagnostics, 'Recorded skill file inventory', collections.skillFiles, inventory.skillFiles);
  return collections;
}

async function validateRootPolicy(root, diagnostics) {
  const rootPolicy = await readUtf8(root, ROOT_POLICY_PATH);
  for (const policyId of REQUIRED_POLICY_IDS) {
    if (!rootPolicy.includes(policyId)) diagnostics.push(namedMessages.missingRootPolicy(policyId));
  }
  for (const commandMarker of REQUIRED_COMMAND_MARKERS) {
    if (!rootPolicy.includes(commandMarker)) diagnostics.push(namedMessages.missingCommand(commandMarker));
  }
  if (PLACEHOLDER_PATTERNS.some(pattern => pattern.test(rootPolicy))) diagnostics.push(namedMessages.rootPlaceholder);
}

function validateSkills(record, inventory, diagnostics) {
  if (record.skillsInstalled !== false) diagnostics.push(namedMessages.skillsMustBeDisabled);
  if (inventory.skillDirectories.length > 0) diagnostics.push(namedMessages.skillDirectories(inventory.skillDirectories));
}

async function validatePreservedPrefix(root, record, diagnostics) {
  const preservation = record.preservedRoot;
  if (!preservation || preservation.path !== ROOT_POLICY_PATH || !Number.isInteger(preservation.prefixBytes) || preservation.prefixBytes <= 0 || !/^[a-f0-9]{64}$/i.test(preservation.sha256 ?? '')) {
    diagnostics.push(namedMessages.invalidRootRecord);
    return false;
  }
  const rootBytes = await readFile(await resolveRegularFile(root, ROOT_POLICY_PATH));
  if (rootBytes.length < preservation.prefixBytes) {
    diagnostics.push(namedMessages.shortRoot(preservation.prefixBytes));
    return false;
  }
  const prefix = rootBytes.subarray(0, preservation.prefixBytes);
  const actualHash = createHash('sha256').update(prefix).digest('hex');
  if (actualHash !== preservation.sha256) {
    diagnostics.push(namedMessages.prefixMismatch(preservation.sha256, actualHash));
    return false;
  }
  return true;
}

function validateLocalPolicyText(repositoryPath, content, diagnostics) {
  const missingMarkers = LOCAL_POLICY_MARKERS.filter(marker => !marker.test(content));
  if (missingMarkers.length > 0) {
    diagnostics.push(namedMessages.incompletePolicy(repositoryPath));
  }
  if (PLACEHOLDER_PATTERNS.some(pattern => pattern.test(content))) {
    diagnostics.push(namedMessages.unresolvedPlaceholder(repositoryPath));
  }
}

async function validateLocalPolicies(root, projectPaths, modulePaths, diagnostics) {
  const policyPaths = [
    ...projectPaths.map(projectPath => join(dirname(projectPath), 'AGENTS.md').split(sep).join('/')),
    ...modulePaths.map(modulePath => `${modulePath}/AGENTS.md`),
  ];
  for (const policyPath of policyPaths) {
    if (!(await exists(root, policyPath))) {
      diagnostics.push(namedMessages.missingPolicy(policyPath));
      continue;
    }
    validateLocalPolicyText(policyPath, await readUtf8(root, policyPath), diagnostics);
  }
}

async function validateRequiredFiles(root, diagnostics) {
  for (const repositoryPath of REQUIRED_FILES) {
    if (!(await exists(root, repositoryPath))) {
      diagnostics.push(namedMessages.missingGovernanceFile(repositoryPath));
      continue;
    }
    const content = await readUtf8(root, repositoryPath);
    if (PLACEHOLDER_PATTERNS.some(pattern => pattern.test(content))) {
      diagnostics.push(namedMessages.unresolvedPlaceholder(repositoryPath));
    }
  }
}

async function validate(root) {
  const diagnostics = [];
  const loadedRecord = JSON.parse(await readUtf8(root, RECORD_PATH));
  const record = loadedRecord && typeof loadedRecord === 'object' && !Array.isArray(loadedRecord) ? loadedRecord : {};
  if (record !== loadedRecord) diagnostics.push(namedMessages.invalidRecord);
  const inventory = { projects: [], skillFiles: [], skillDirectories: [], workingFiles: [], flatFeatureSources: [] };
  await walkRepository(root, root, inventory);
  inventory.projects.sort();
  inventory.skillFiles.sort();
  inventory.skillDirectories.sort();
  inventory.workingFiles.sort();
  for (const repositoryPath of inventory.workingFiles) diagnostics.push(namedMessages.workingFile(repositoryPath));
  for (const repositoryPath of inventory.flatFeatureSources) diagnostics.push(namedMessages.flatFeatureSource(repositoryPath));
  const collections = await validateRecordAndInventory(root, record, inventory, diagnostics);
  await validateRootPolicy(root, diagnostics);
  await validateRequiredFiles(root, diagnostics);
  await validateLocalPolicies(root, collections.projects, collections.modules, diagnostics);
  validateSkills(record, inventory, diagnostics);
  return {
    diagnostics,
    projectCount: inventory.projects.length,
    moduleCount: collections.modules.length,
    skillFileCount: inventory.skillFiles.length,
  };
}

async function main() {
  const root = parseRoot(process.argv.slice(2));
  const result = await validate(root);
  if (result.diagnostics.length > 0) {
    for (const diagnostic of result.diagnostics) process.stderr.write(`${namedMessages.failurePrefix}${diagnostic}\n`);
    process.stderr.write(namedMessages.failureSummary(result.diagnostics.length));
    process.exitCode = 1;
    return;
  }
  process.stdout.write(namedMessages.success(result.projectCount, result.moduleCount));
}

main().catch(error => {
  process.stderr.write(`${namedMessages.failurePrefix}${error.message}\n`);
  process.exitCode = 1;
});
