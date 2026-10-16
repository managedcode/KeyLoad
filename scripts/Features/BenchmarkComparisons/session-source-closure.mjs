import path from 'node:path';
import { readFile, lstat, realpath } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { requireSession } from './session-contract.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const feature = 'site/Features/BenchmarkComparisons/';
const entries = [
  'site/scripts/build.mjs', 'scripts/Features/BenchmarkComparisons/site-isolated-github-cli.mjs',
  'scripts/Features/BenchmarkComparisons/site-isolated-github-receipt.mjs',
  'scripts/Features/BenchmarkComparisons/session-source-closure.mjs',
  'scripts/Features/BenchmarkComparisons/document-functional-trx.ps1',
  'scripts/Features/CodeQuality/functional-coverage.shared.ps1',
  'scripts/Features/CodeQuality/functional-coverage.parse.ps1',
  'scripts/Features/CodeQuality/functional-coverage.native-merge.trx.ps1',
  ...['bootstrap', 'cluster-scene', 'scene-geometry', 'scene-lifecycle', 'scene-observers',
    'contracts', 'build-site', 'measured-build', 'isolated-contracts', 'isolated-http', 'isolated-loader',
    'isolated-projection', 'isolated-measurements', 'isolated-controls', 'isolated-lab', 'isolated-view',
    'measurements', 'measurement-loader'].map(name => feature + name + '.mjs'),
];
// Only literal relative imports are executable dependencies. Platform built-ins carry no repository source.
export async function sessionSourceClosure(root = repository) {
  const seen = new Set();
  const pending = [...entries];
  while (pending.length > 0) {
    const relative = pending.pop();
    if (seen.has(relative)) continue;
    requireSession(seen.size < 256 && /^(?:scripts\/Features\/(?:BenchmarkComparisons|CodeQuality)|site\/Features\/BenchmarkComparisons|site\/scripts)\/[A-Za-z0-9_.-]+\.(?:mjs|ps1)$/.test(relative));
    const absolute = path.join(root, relative);
    const info = await lstat(absolute);
    requireSession(info.isFile() && !info.isSymbolicLink() && info.size > 0 && info.size <= 262_144
      && await realpath(absolute) === absolute);
    const source = await readFile(absolute, 'utf8');
    seen.add(relative);
    const patterns = [/\b(?:import|export)\s+(?:\{[^}]*\}|[\w*$,\s]+)\s+from\s*['"]([^'"]+)['"]/g,
      /import\(\s*['"]([^'"]+)['"]\s*\)/g];
    for (const pattern of patterns) for (const matched of source.matchAll(pattern)) {
      const specifier = matched[1];
      if (specifier.startsWith('node:')) continue;
      requireSession(specifier.startsWith('.'));
      const dependency = path.posix.normalize(path.posix.join(path.posix.dirname(relative), specifier));
      // Frozen vendor files have independent byte hashes and original-license verification in verifyVendor.
      if (/^site\/Features\/BenchmarkComparisons\/vendor\/three\/0\.186\.1\/three\.(?:webgpu|core)\.js$/.test(dependency)) continue;
      requireSession(!dependency.startsWith('../') && dependency.endsWith('.mjs'));
      pending.push(dependency);
    }
  }
  return [...seen].sort();
}
export async function verifySessionSourceClosure(file, root = repository) {
  const expected = await sessionSourceClosure(root);
  const text = await readFile(file, 'utf8');
  requireSession(text === expected.join('\n') + '\n');
  return { files: expected.length };
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    requireSession(process.argv.length === 3 && process.argv[2].startsWith('--verify='));
    process.stdout.write(JSON.stringify(await verifySessionSourceClosure(path.resolve(process.argv[2].slice('--verify='.length)))) + '\n');
  } catch { process.stderr.write('Benchmark session source closure rejected.\n'); process.exitCode = 1; }
}
