import { lstat, readFile, readdir } from 'node:fs/promises';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { testOutputPathDiagnostics } from './test-output-paths.mjs';

const SCRIPT_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_ROOT = resolve(SCRIPT_DIRECTORY, '../../..');
const SOLUTION_PATH = 'KeyLoad.slnx';
const MODULE_PATHS = ['docs', 'site', 'scripts', '.github/workflows', 'analyzers', 'build'];
const ROOT_POLICY_PATH = 'AGENTS.md';
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
  'gh workflow run build-and-tests.yml',
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
  'data',
  'backups',
  '.agents',
  '.codex',
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
  missingItem: (label, value) => `${label} is missing: ${value}`,
  unexpectedItem: (label, value) => `${label} is unexpected: ${value}`,
  missingPolicy: value => `Required local policy is missing: ${value}`,
  incompletePolicy: value => `${value} must document purpose, entry points, boundaries, commands, skills policy and protected risks.`,
  unresolvedPlaceholder: value => `${value} contains an unresolved placeholder.`,
  missingGovernanceFile: value => `Required governance file is missing: ${value}`,
  flatFeatureSource: value => `Feature source requires a responsibility folder: ${value}`,
  obsoleteProjectFeatures: value => `Project slices must live directly under the project: ${value}`,
  redundantFeatureWrapper: value => `Single-feature project must place responsibility folders directly under its root: ${value}`,
  workingFile: value => `Working planning file is present: ${value}`,
  missingRootPolicy: value => `Root AGENTS.md is missing required policy ${value}.`,
  missingCommand: value => `Root AGENTS.md is missing the required command: ${value}.`,
  rootPlaceholder: 'Root AGENTS.md contains an unresolved template placeholder.',
  failurePrefix: 'FAIL: ',
  failureSummary: count => `Governance validation failed with ${count} issue(s).\n`,
  success: (projects, modules) => `PASS: current solution inventory, ${projects} projects, ${modules} modules, required governance and local policies.\n`,
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
      if (/^(?:src|tests|benchmarks|analyzers|build)\/[^/]+\/Features$/.test(toRepositoryPath(root, absolutePath))) {
        inventory.flatFeatureSources.push(toRepositoryPath(root, absolutePath));
      }
      await walkRepository(root, absolutePath, inventory);
      continue;
    }
    if (!entry.isFile()) continue;
    const repositoryPath = toRepositoryPath(root, absolutePath);
    if (entry.name.endsWith('.cs')) inventory.sourceFiles.push(repositoryPath);
    if (WORKING_FILE_SUFFIXES.some(suffix => entry.name.endsWith(suffix))) inventory.workingFiles.push(repositoryPath);
    const projectSlice = /^(?:src|tests|benchmarks|analyzers|build)\/[^/]+\/([^/]+)\/[^/]+\.cs$/.exec(repositoryPath);
    const specificationSlice = projectSlice?.[1] === 'Restore' ? 'BackupRestore' : projectSlice?.[1];
    if (projectSlice && await exists(root, `docs/Features/${specificationSlice}.md`)) inventory.flatFeatureSources.push(repositoryPath);
    if (entry.name.endsWith('.csproj')) inventory.projects.push(repositoryPath);
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

async function validateSolutionInventory(root, inventory, diagnostics) {
  const solution = await readUtf8(root, SOLUTION_PATH);
  const declaredProjects = [...solution.matchAll(/<Project\s+Path="([^"]+)"\s*\/>/g)]
    .map(match => match[1]).sort();
  addMissingItems(diagnostics, 'Project inventory', declaredProjects, inventory.projects);
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

async function validateProjectResponsibilities(root, inventory, diagnostics) {
  for (const projectPath of inventory.projects) {
    const prefix = `${dirname(projectPath)}/`;
    const folders = new Set(inventory.sourceFiles
      .filter(source => source.startsWith(prefix))
      .map(source => source.slice(prefix.length).split('/'))
      .filter(parts => parts.length > 1)
      .map(parts => parts[0]));
    if (folders.size !== 1) continue;
    const [folder] = folders;
    const specification = folder === 'Restore' ? 'BackupRestore' : folder;
    if (await exists(root, `docs/Features/${specification}.md`)) {
      diagnostics.push(namedMessages.redundantFeatureWrapper(`${prefix}${folder}`));
    }
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
  const inventory = { projects: [], sourceFiles: [], workingFiles: [], flatFeatureSources: [] };
  await walkRepository(root, root, inventory);
  inventory.projects.sort();
  inventory.workingFiles.sort();
  for (const repositoryPath of inventory.workingFiles) diagnostics.push(namedMessages.workingFile(repositoryPath));
  for (const repositoryPath of inventory.flatFeatureSources) diagnostics.push(repositoryPath.endsWith('/Features')
    ? namedMessages.obsoleteProjectFeatures(repositoryPath) : namedMessages.flatFeatureSource(repositoryPath));
  await validateSolutionInventory(root, inventory, diagnostics);
  await validateProjectResponsibilities(root, inventory, diagnostics);
  await validateRootPolicy(root, diagnostics);
  for (const repositoryPath of inventory.sourceFiles) {
    if (!/^(?:tests|benchmarks)\//.test(repositoryPath)) continue;
    diagnostics.push(...testOutputPathDiagnostics(repositoryPath, await readUtf8(root, repositoryPath)));
  }
  await validateRequiredFiles(root, diagnostics);
  await validateLocalPolicies(root, inventory.projects, MODULE_PATHS, diagnostics);
  return {
    diagnostics,
    projectCount: inventory.projects.length,
    moduleCount: MODULE_PATHS.length,
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
