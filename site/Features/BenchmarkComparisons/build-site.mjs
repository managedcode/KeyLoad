import { readFile, readdir, mkdir, copyFile, writeFile, lstat, realpath, rm } from 'node:fs/promises';
import { resolve, dirname, join, relative, isAbsolute, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { gzipSync } from 'node:zlib';
import { CONFIG } from './contracts.mjs';
import { validateCatalog, validateReport } from './measurement-loader.mjs';
import { produceIsolatedProjection } from './isolated-projection.mjs';
import { validateIsolatedCatalog, validateIsolatedProjection } from './isolated-loader.mjs';
import { readBytes } from '../../../scripts/Features/BenchmarkComparisons/aggregate-files.mjs';

const BUILD = Object.freeze({
  encoding: 'utf8', hash: 'sha256', hex: 'hex', feature: 'Features/BenchmarkComparisons', sourceParent: '../..',
  vendor: 'vendor/three/0.186.1', manifest: 'manifest.json', three: 'three', version: '0.186.1',
  license: 'MIT', sourceCommit: '9b4a2ac29c63ccb43fd51c5661f2f873ac2c39b8',
  integrity: 'sha512-blFeqb49wRCSGUGj7gtpfnSGHy2lwDk94RhUmS1c/hTby70kvChbWpkJ4Pm1390LqzzvTmzgXKHPEafJwCb8jA==',
  assets: ['isolated-contracts.mjs', 'isolated-metadata.mjs', 'isolated-metrics-validation.mjs',
    'isolated-report-validation.mjs', 'isolated-http.mjs', 'isolated-loader.mjs',
    'isolated-measurements.mjs', 'isolated-view.mjs', 'isolated-controls.mjs', 'isolated-lab.mjs', 'contracts.mjs', 'bootstrap.mjs', 'measurements.mjs', 'measurement-loader.mjs',
    'benchmark-lab.mjs', 'benchmark-chart.mjs', 'benchmark-profiles.mjs', 'cluster-scene.mjs',
    'scene-geometry.mjs', 'scene-lifecycle.mjs', 'scene-observers.mjs', 'styles.css', 'brand.css', 'tokens.css', 'scene.css', 'assets/cluster-poster.svg'],
  vendorFiles: ['three.webgpu.js', 'three.core.js', 'LICENSE'],
  vendorHashes: ['15cfce5c653541704fd9a3463c39d3e8b854bb6265ccd854d7cfe74090625cc6',
    '9edde002b066a9a05676a6127f67735b62baf399bdea529f2f7e31657da769e6',
    '8b378ebe60e2fe500158cb0ac71cb5e8b7d92953c2abcc63a0eb90499653b5bc'],
  html: 'index.html', favicon: 'favicon.svg', data: 'data', runs: 'runs', catalog: 'catalog.json',
  reports: '--reports', isolated: '--isolated', output: '--output', revision: '--revision', evidence: '--evidence-url', site: '--site-revision',
  preview: 'local_preview', committed: 'committed_source', smoke: 'smoke',
  empty: '', equals: '=', newline: '\n', missing: 'ENOENT', javascriptExtension: '.mjs', cssExtension: '.css',
  noJekyll: '.nojekyll', cname: 'CNAME', domain: 'www.keyload.cloud\n', robotsFile: 'robots.txt', sitemapFile: 'sitemap.xml',
  smokeLabel: ' · correctness smoke', zero: 0, one: 1, jsonIndent: 2,
  authoredJsLimit: 40960, cssLimit: 20480,
  compression: 'compression', nodeVersion: 'nodeVersion', zlibVersion: 'zlibVersion', vendorReceipt: 'vendor',
  recordedGzipBytes: 'recordedGzipBytes', runtimeGzipBytes: 'runtimeGzipBytes', safeGzip: 'isSafeInteger',
  path: 'path', bytes: 'bytes',
  parent: '..', staticEvidence: '<!-- KEYLOAD_STATIC_EVIDENCE -->',
  documentationBlob: '/blob/main/docs', documentationTree: '/tree/main/docs',
  robots: 'User-agent: *\nAllow: /\nSitemap: https://www.keyload.cloud/sitemap.xml\n',
  sitemap: '<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>https://www.keyload.cloud/</loc></url></urlset>\n',
});
const ERRORS = Object.freeze({
  argument: 'Use unique known --name=value arguments.', required: 'Reports, isolated output, measured revision and evidence URL are required.',
  output: 'Output must be a nonexistent isolated directory outside source/reports and their ancestors.',
  symlink: 'Symlinks and non-regular evidence/assets are not accepted.', vendor: 'Official Three vendor manifest or bytes differ.',
  profiles: 'At least one complete verified workload profile is required.', budget: 'Authored critical asset budget exceeded.',
});
const source = resolve(dirname(fileURLToPath(import.meta.url)), BUILD.sourceParent);
const feature = join(source, BUILD.feature);
const hash = bytes => createHash(BUILD.hash).update(bytes).digest(BUILD.hex);
const knownArguments = new Set([BUILD.reports, BUILD.isolated, BUILD.output, BUILD.revision, BUILD.evidence, BUILD.site]);

function parseArguments(argv) {
  const args = {};
  for (const item of argv) {
    const separator = item.indexOf(BUILD.equals);
    const name = item.slice(0, separator);
    if (separator < BUILD.zero || !knownArguments.has(name) || name in args) throw new Error(ERRORS.argument);
    args[name] = item.slice(separator + BUILD.one);
  }
  if ((!args[BUILD.reports] && !args[BUILD.isolated]) || !args[BUILD.output] || !CONFIG.revisionPattern.test(args[BUILD.revision]) ||
      !CONFIG.evidencePattern.test(args[BUILD.evidence]) ||
      (BUILD.site in args && !CONFIG.revisionPattern.test(args[BUILD.site]))) throw new Error(ERRORS.required);
  return args;
}

function contains(parent, child) {
  const distance = relative(parent, child);
  return distance === BUILD.empty || (distance !== BUILD.parent && !distance.startsWith(BUILD.parent + sep) && !isAbsolute(distance));
}

async function assertRegular(path) {
  const stat = await lstat(path);
  if (!stat.isFile() || stat.isSymbolicLink() || await realpath(path) !== resolve(path)) throw new Error(ERRORS.symlink);
}

async function preparePaths(args) {
  const reports = resolve(args[BUILD.reports] ?? args[BUILD.isolated]);
  const output = resolve(args[BUILD.output]);
  if (await realpath(reports) !== reports || !(await lstat(reports)).isDirectory()) throw new Error(ERRORS.symlink);
  const inputs = [source, reports, ...(args[BUILD.isolated] ? [resolve(args[BUILD.isolated])] : [])];
  if (inputs.some(path => contains(path, output) || contains(output, path))) throw new Error(ERRORS.output);
  try {
    await lstat(output);
    throw new Error(ERRORS.output);
  } catch (error) {
    if (error.code !== BUILD.missing) throw error;
  }
  if (await realpath(dirname(output)) !== dirname(output)) throw new Error(ERRORS.symlink);
  return { reports, output };
}

async function verifyVendor() {
  const root = join(feature, BUILD.vendor);
  await assertRegular(join(root, BUILD.manifest));
  const manifest = JSON.parse(await readFile(join(root, BUILD.manifest), BUILD.encoding));
  if (manifest.package !== BUILD.three || manifest.version !== BUILD.version || manifest.license !== BUILD.license ||
      manifest.sourceCommit !== BUILD.sourceCommit || manifest.integrity !== BUILD.integrity ||
      manifest.files?.length !== BUILD.vendorFiles.length) throw new Error(ERRORS.vendor);
  const vendor = [];
  for (const [index, name] of BUILD.vendorFiles.entries()) {
    const path = join(root, name);
    await assertRegular(path);
    const bytes = await readFile(path);
    const recorded = manifest.files.find(file => file.path === name);
    const sha256 = hash(bytes);
    const runtimeGzipBytes = gzipSync(bytes).length;
    if (sha256 !== BUILD.vendorHashes[index] || recorded?.sha256 !== BUILD.vendorHashes[index] ||
        recorded.bytes !== bytes.length || !Number[BUILD.safeGzip](recorded.gzipBytes) ||
        !(recorded.gzipBytes > BUILD.zero)) throw new Error(ERRORS.vendor);
    vendor.push({ [BUILD.path]: name, [BUILD.hash]: sha256, [BUILD.bytes]: bytes.length,
      [BUILD.recordedGzipBytes]: recorded.gzipBytes, [BUILD.runtimeGzipBytes]: runtimeGzipBytes });
  }

  return { [BUILD.nodeVersion]: process.versions.node, [BUILD.zlibVersion]: process.versions.zlib,
    [BUILD.vendorReceipt]: vendor };
}

async function verifyAssets() {
  let javascript = BUILD.zero;
  let css = BUILD.zero;
  for (const name of [BUILD.html, ...BUILD.assets]) {
    const path = join(feature, name);
    await assertRegular(path);
    const bytes = await readFile(path);
    if (name.endsWith(BUILD.javascriptExtension)) javascript += gzipSync(bytes).length;
    if (name.endsWith(BUILD.cssExtension)) css += gzipSync(bytes).length;
  }
  await assertRegular(join(source, BUILD.favicon));
  if (javascript > BUILD.authoredJsLimit || css > BUILD.cssLimit) throw new Error(ERRORS.budget);
  return { javascriptGzipBytes: javascript, cssGzipBytes: css };
}

async function readProfile(reports, folder, args) {
  if (!CONFIG.idPattern.test(folder.name) || folder.isSymbolicLink() || !folder.isDirectory()) throw new Error(ERRORS.symlink);
  const root = join(reports, folder.name);
  if (await realpath(root) !== root) throw new Error(ERRORS.symlink);
  for (const file of Object.values(CONFIG.files)) await assertRegular(join(root, file));
  const bytes = await readFile(join(root, CONFIG.files.json));
  if (bytes.length > CONFIG.maximumReportBytes) throw new Error(ERRORS.profiles);
  const report = validateReport(JSON.parse(bytes.toString(BUILD.encoding)), args[BUILD.revision]);
  const options = report.options;
  const label = `${options.payloadBytes / CONFIG.units.bytesPerKiB} KiB · ${options.concurrency} clients · ${options.documents} documents · ${options.graphDepth} graph hops`;
  return { id: folder.name, label: label + (folder.name === BUILD.smoke ? BUILD.smokeLabel : BUILD.empty),
    report: `${BUILD.runs}/${folder.name}/${CONFIG.files.json}`, evidenceUrl: args[BUILD.evidence],
    startedAt: report.startedAt, sourceRevision: report.sourceRevision, sha256: hash(bytes) };
}

async function readCatalog(reports, args) {
  const folders = await readdir(reports, { withFileTypes: true });
  const runs = [];
  for (const folder of folders) {
    if (folder.isDirectory() || folder.isSymbolicLink()) runs.push(await readProfile(reports, folder, args));
  }
  if (!runs.length) throw new Error(ERRORS.profiles);
  runs.sort((a, b) => Number(a.id === BUILD.smoke) - Number(b.id === BUILD.smoke) || a.id.localeCompare(b.id));
  return validateCatalog({ schemaVersion: CONFIG.catalogSchemaVersion, generatedAt: new Date().toISOString(),
    siteSourceKind: args[BUILD.site] ? BUILD.committed : BUILD.preview, siteSourceRevision: args[BUILD.site] ?? null,
    measuredSourceRevision: args[BUILD.revision], evidenceUrl: args[BUILD.evidence], runs });
}

async function copyAsset(output, path, target = path) {
  const destination = join(output, target);
  await mkdir(dirname(destination), { recursive: true });
  await copyFile(join(source, path), destination);
}

async function emit(output, reports, catalog, isolated) {
  await emitHtml(output, catalog, isolated);
  await copyAsset(output, BUILD.favicon);
  for (const asset of BUILD.assets) await copyAsset(output, `${BUILD.feature}/${asset}`);
  for (const file of [...BUILD.vendorFiles, BUILD.manifest]) await copyAsset(output, `${BUILD.feature}/${BUILD.vendor}/${file}`);
  for (const run of catalog.runs) {
    const destination = join(output, BUILD.data, BUILD.runs, run.id);
    await mkdir(destination, { recursive: true });
    for (const file of Object.values(CONFIG.files)) await copyFile(join(reports, run.id, file), join(destination, file));
    if (hash(await readFile(join(destination, CONFIG.files.json))) !== run.sha256) throw new Error(ERRORS.profiles);
  }
  if (catalog.runs.length > 0) {
    await writeFile(join(output, BUILD.data, BUILD.catalog), JSON.stringify(catalog, null, BUILD.jsonIndent) + BUILD.newline);
  }
  if (isolated) await emitIsolated(output, isolated);
  await writeFile(join(output, BUILD.noJekyll), BUILD.empty);
  await writeFile(join(output, BUILD.cname), BUILD.domain);
  await writeFile(join(output, BUILD.robotsFile), BUILD.robots);
  await writeFile(join(output, BUILD.sitemapFile), BUILD.sitemap);
}

async function emitHtml(output, catalog, isolated) {
  let html = await readFile(join(feature, BUILD.html), BUILD.encoding);
  if (catalog.siteSourceRevision) {
    html = html.replaceAll(BUILD.documentationBlob, `/blob/${catalog.siteSourceRevision}/docs`)
      .replaceAll(BUILD.documentationTree, `/tree/${catalog.siteSourceRevision}/docs`);
  }
  if (isolated) {
    html = html.replace('id="isolated-lab" class="benchmark-section" aria-labelledby="isolated-title" hidden',
      'id="isolated-lab" class="benchmark-section" aria-labelledby="isolated-title" data-isolated-catalog="./data/isolated-catalog.json"');
    html = html.replace('<!-- KEYLOAD_ISOLATED_EVIDENCE -->',
      '<p><a href="./data/isolated/aggregate.json" download>Native evidence manifest</a> · <a href="'
      + isolated.catalog.evidenceUrl + '">GitHub native comparison evidence</a></p>');
  }
  if (catalog.runs.length === 0) {
    html = html.replace('<body', '<body data-historical-unavailable="true"');
    html = html.replace('id="benchmarks"', 'id="benchmarks" hidden');
  }
  const links = catalog.runs.map(run => `<li>${run.label} · <a href="./${BUILD.data}/${run.report}" download>Raw JSON</a> · <a href="${run.evidenceUrl}">GitHub comparison evidence</a></li>`).join(BUILD.empty);
  if (!html.includes(BUILD.staticEvidence)) throw new Error(ERRORS.profiles);
  await writeFile(join(output, BUILD.html), html.replace(BUILD.staticEvidence, `<ul>${links}</ul>`));
}

async function prepareIsolated(args) {
  if (!CONFIG.revisionPattern.test(args[BUILD.site] ?? BUILD.empty)) throw new Error(ERRORS.required);
  const input = resolve(args[BUILD.isolated]);
  if (await realpath(input) !== input || !(await lstat(input)).isDirectory()) throw new Error(ERRORS.symlink);
  const manifestPath = join(input, 'aggregate.json');
  const manifest = await readIsolatedManifest(manifestPath);
  const projection = await produceIsolatedProjection({ input });
  if (!manifest.equals(await readIsolatedManifest(manifestPath))) throw new Error(ERRORS.profiles);
  const bytes = Buffer.from(JSON.stringify(projection) + BUILD.newline);
  if (bytes.length > 4_194_304) throw new Error(ERRORS.budget);
  const catalog = validateIsolatedCatalog({ schemaVersion: 1, generatedAt: new Date().toISOString(),
    siteSourceRevision: args[BUILD.site], measuredSourceRevision: projection.cohort.sourceRevision,
    evidenceUrl: 'https://github.com/managedcode/KeyLoad/actions/runs/' + projection.cohort.runId,
    cohort: projection.cohort, aggregate: { path: 'isolated/aggregate.json', sha256: hash(manifest) },
    projection: { path: 'isolated/projection.json', sha256: hash(bytes) }, rawLocation: 'githubActionsArtifacts' });
  validateIsolatedProjection(projection, catalog);
  if (!args[BUILD.reports] && (args[BUILD.revision] !== catalog.measuredSourceRevision || args[BUILD.evidence] !== catalog.evidenceUrl)) {
    throw new Error(ERRORS.profiles);
  }
  return { catalog, bytes, manifest };
}

async function readIsolatedManifest(path) {
  return readBytes(path, 4_194_304);
}

async function emitIsolated(output, isolated) {
  const directory = join(output, BUILD.data, 'isolated');
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, 'aggregate.json'), isolated.manifest, { flag: 'wx' });
  await writeFile(join(directory, 'projection.json'), isolated.bytes, { flag: 'wx' });
  const catalog = JSON.stringify(isolated.catalog) + BUILD.newline;
  if (Buffer.byteLength(catalog) > 65_536) throw new Error(ERRORS.budget);
  await writeFile(join(output, BUILD.data, 'isolated-catalog.json'), catalog, { flag: 'wx' });
}

export async function buildSite(argv) {
  const args = parseArguments(argv);
  const { reports, output } = await preparePaths(args);
  const compression = await verifyVendor();
  const sizes = await verifyAssets();
  const isolated = args[BUILD.isolated] ? await prepareIsolated(args) : null;
  const catalog = args[BUILD.reports] ? await readCatalog(reports, args)
    : { runs: [], siteSourceRevision: args[BUILD.site] };
  if (catalog.runs.length === 0 && isolated === null) throw new Error(ERRORS.profiles);
  await mkdir(output);
  try {
    await emit(output, reports, catalog, isolated);
    return { output, profiles: catalog.runs.length, ...sizes, [BUILD.compression]: compression };
  } catch (error) {
    await rm(output, { recursive: true, force: true });
    throw error;
  }
}
