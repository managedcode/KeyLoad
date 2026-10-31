import { readFile, mkdir, copyFile, writeFile, lstat, realpath, rm } from 'node:fs/promises';
import { resolve, dirname, join, relative, isAbsolute, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { gzipSync } from 'node:zlib';
import { CONFIG } from './contracts.mjs';
import { validateMerged } from './results-contract.mjs';
import { renderResults } from './results-view.mjs';

const META_ASSET = Object.freeze({ manifest: 'assets/site.webmanifest', manifestOutput: 'site.webmanifest',
  ogImage: 'assets/og-image.png', ogImageOutput: 'og-image.png', ogSource: 'assets/og-image.svg' });
export const BUILD = Object.freeze({
  encoding: 'utf8', hash: 'sha256', hex: 'hex', feature: 'Features/BenchmarkComparisons', sourceParent: '../..',
  vendor: 'vendor/three/0.186.1', manifest: 'manifest.json', three: 'three', version: '0.186.1',
  license: 'MIT', sourceCommit: '9b4a2ac29c63ccb43fd51c5661f2f873ac2c39b8',
  integrity: 'sha512-blFeqb49wRCSGUGj7gtpfnSGHy2lwDk94RhUmS1c/hTby70kvChbWpkJ4Pm1390LqzzvTmzgXKHPEafJwCb8jA==',
  // Browser assets only; the results contract and markup run at build time.
  commonAssets: ['contracts.mjs', 'bootstrap.mjs', 'cluster-scene.mjs', 'scene-geometry.mjs',
    'scene-lifecycle.mjs', 'scene-observers.mjs', 'results-controls.mjs', 'styles.css', 'brand.css', 'tokens.css', 'scene.css',
    'assets/agent-context.png'],
  rootAssets: ['favicon.ico', 'favicon-32x32.png', 'favicon-96x96.png', 'apple-touch-icon.png', 'icon-192.png', 'icon-512.png'],
  metadataAssets: [META_ASSET.manifest, META_ASSET.ogImage, META_ASSET.ogSource],
  metadataCopies: [[META_ASSET.manifest, META_ASSET.manifestOutput], [META_ASSET.ogImage, META_ASSET.ogImageOutput]],
  vendorFiles: ['three.webgpu.js', 'three.core.js', 'LICENSE'],
  vendorHashes: ['15cfce5c653541704fd9a3463c39d3e8b854bb6265ccd854d7cfe74090625cc6',
    '9edde002b066a9a05676a6127f67735b62baf399bdea529f2f7e31657da769e6',
    '8b378ebe60e2fe500158cb0ac71cb5e8b7d92953c2abcc63a0eb90499653b5bc'],
  html: 'index.html', favicon: 'favicon.svg', data: 'data', resultsFile: 'results.json',
  results: '--results', noResults: 'none', output: '--output', site: '--site-revision',
  empty: '', equals: '=', missing: 'ENOENT', javascriptExtension: '.mjs', cssExtension: '.css',
  noJekyll: '.nojekyll', cname: 'CNAME', domain: 'www.keyload.cloud\n', robotsFile: 'robots.txt', sitemapFile: 'sitemap.xml',
  one: 1, authoredJsLimit: 40960, cssLimit: 20480,
  compression: 'compression', nodeVersion: 'nodeVersion', zlibVersion: 'zlibVersion', vendorReceipt: 'vendor',
  recordedGzipBytes: 'recordedGzipBytes', runtimeGzipBytes: 'runtimeGzipBytes', safeGzip: 'isSafeInteger',
  path: 'path', bytes: 'bytes', parent: '..', placeholder: '<!-- KEYLOAD_BENCHMARK_RESULTS -->',
  emptyState: '<p class="empty-state">No benchmark results are available yet. Check back after a completed comparison run.</p>',
  documentationBlob: '/blob/main/docs', documentationTree: '/tree/main/docs',
  robots: 'User-agent: *\nAllow: /\nDisallow: /admin/\nSitemap: https://www.keyload.cloud/sitemap.xml\n',
  sitemap: '<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>https://www.keyload.cloud/</loc></url></urlset>\n',
});
const ERRORS = Object.freeze({
  argument: 'Use --results=<results.json>|none --output=<new directory> --site-revision=<40-hex revision>.',
  output: 'Output must be a nonexistent directory outside the site source.',
  symlink: 'Symlinks and non-regular results or assets are not accepted.',
  vendor: 'Official Three vendor manifest or bytes differ.',
  budget: 'Authored asset budget exceeded.',
  html: 'The benchmark results placeholder is missing.',
});
export const source = resolve(dirname(fileURLToPath(import.meta.url)), BUILD.sourceParent);
export const feature = join(source, BUILD.feature);
const hash = bytes => createHash(BUILD.hash).update(bytes).digest(BUILD.hex);

function parseArguments(argv) {
  const args = {};
  for (const item of argv) {
    const separator = item.indexOf(BUILD.equals);
    const name = item.slice(0, separator);
    if (separator < 0 || ![BUILD.results, BUILD.output, BUILD.site].includes(name) || name in args) throw new Error(ERRORS.argument);
    args[name] = item.slice(separator + BUILD.one);
  }
  if (!args[BUILD.results] || !args[BUILD.output] || !CONFIG.revisionPattern.test(args[BUILD.site] ?? BUILD.empty)) {
    throw new Error(ERRORS.argument);
  }
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

async function prepareOutputPath(value) {
  const output = resolve(value);
  if (contains(source, output) || contains(output, source)) throw new Error(ERRORS.output);
  try {
    await lstat(output);
    throw new Error(ERRORS.output);
  } catch (error) {
    if (error.code !== BUILD.missing) throw error;
  }
  if (await realpath(dirname(output)) !== dirname(output)) throw new Error(ERRORS.symlink);
  return output;
}

// The merged file is validated with the same contract as the per-job files and published unchanged.
async function readResults(value) {
  if (value === BUILD.noResults) return null;
  const path = resolve(value);
  await assertRegular(path);
  const bytes = await readFile(path);
  return { bytes, merged: validateMerged(JSON.parse(bytes.toString(BUILD.encoding)), value) };
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
        recorded.bytes !== bytes.length || !Number[BUILD.safeGzip](recorded.gzipBytes) || !(recorded.gzipBytes > 0)) {
      throw new Error(ERRORS.vendor);
    }
    vendor.push({ [BUILD.path]: name, [BUILD.hash]: sha256, [BUILD.bytes]: bytes.length,
      [BUILD.recordedGzipBytes]: recorded.gzipBytes, [BUILD.runtimeGzipBytes]: runtimeGzipBytes });
  }
  return { [BUILD.nodeVersion]: process.versions.node, [BUILD.zlibVersion]: process.versions.zlib,
    [BUILD.vendorReceipt]: vendor };
}

async function verifyAssets() {
  let javascript = 0;
  let css = 0;
  for (const name of [BUILD.html, ...BUILD.commonAssets]) {
    const path = join(feature, name);
    await assertRegular(path);
    const bytes = await readFile(path);
    if (name.endsWith(BUILD.javascriptExtension)) javascript += gzipSync(bytes).length;
    if (name.endsWith(BUILD.cssExtension)) css += gzipSync(bytes).length;
  }
  await assertRegular(join(source, BUILD.favicon));
  for (const name of BUILD.rootAssets) await assertRegular(join(source, name));
  for (const name of BUILD.metadataAssets) await assertRegular(join(feature, name));
  if (javascript > BUILD.authoredJsLimit || css > BUILD.cssLimit) throw new Error(ERRORS.budget);
  return { javascriptGzipBytes: javascript, cssGzipBytes: css };
}

async function copyAsset(output, path, target = path) {
  const destination = join(output, target);
  await mkdir(dirname(destination), { recursive: true });
  await copyFile(join(source, path), destination);
}

async function emitHtml(output, siteRevision, results) {
  let html = await readFile(join(feature, BUILD.html), BUILD.encoding);
  html = html.replaceAll(BUILD.documentationBlob, `/blob/${siteRevision}/docs`)
    .replaceAll(BUILD.documentationTree, `/tree/${siteRevision}/docs`);
  if (!html.includes(BUILD.placeholder)) throw new Error(ERRORS.html);
  const markup = results === null ? BUILD.emptyState : renderResults(results.merged);
  await writeFile(join(output, BUILD.html), html.replace(BUILD.placeholder, () => markup));
}

async function emit(output, siteRevision, results) {
  await emitHtml(output, siteRevision, results);
  await copyAsset(output, BUILD.favicon);
  for (const asset of BUILD.rootAssets) await copyAsset(output, asset);
  for (const [asset, target] of BUILD.metadataCopies) await copyAsset(output, `${BUILD.feature}/${asset}`, target);
  for (const asset of BUILD.commonAssets) await copyAsset(output, `${BUILD.feature}/${asset}`);
  for (const file of [...BUILD.vendorFiles, BUILD.manifest]) await copyAsset(output, `${BUILD.feature}/${BUILD.vendor}/${file}`);
  if (results !== null) {
    await mkdir(join(output, BUILD.data));
    await writeFile(join(output, BUILD.data, BUILD.resultsFile), results.bytes, { flag: 'wx' });
  }
  await writeFile(join(output, BUILD.noJekyll), BUILD.empty);
  await writeFile(join(output, BUILD.cname), BUILD.domain);
  await writeFile(join(output, BUILD.robotsFile), BUILD.robots);
  await writeFile(join(output, BUILD.sitemapFile), BUILD.sitemap);
}

export async function buildSite(argv) {
  const args = parseArguments(argv);
  const results = await readResults(args[BUILD.results]);
  const output = await prepareOutputPath(args[BUILD.output]);
  const compression = await verifyVendor();
  const sizes = await verifyAssets();
  await mkdir(output);
  try {
    await emit(output, args[BUILD.site], results);
    return { output, siteRevision: args[BUILD.site], results: results?.merged.results.length ?? 0,
      ...sizes, [BUILD.compression]: compression };
  } catch (error) {
    await rm(output, { recursive: true, force: true });
    throw error;
  }
}
