import { readFile, mkdir, writeFile, lstat, realpath, rm } from 'node:fs/promises';
import { resolve, dirname, join } from 'node:path';
import { CONFIG } from './contracts.mjs';
import { BUILD, copyAsset, feature, hash, prepareOutputPath, source, verifyAssets, verifyVendor } from './build-site.mjs';

const ERRORS = Object.freeze({
  argument: 'Use unique known --name=value arguments.',
  required: 'Isolated aggregate, output and matching source revisions are required.',
  symlink: 'Symlinks and non-regular evidence/assets are not accepted.',
  budget: 'Authored asset budget or isolated evidence size bound exceeded.',
  cohort: 'Isolated aggregate identity or projection is invalid.',
  html: 'Current isolated evidence placeholder is missing.',
  mode: 'Unknown benchmark mode.',
});

function parseMeasuredArguments(argv) {
  const args = {};
  const known = new Set([BUILD.isolated, BUILD.output, BUILD.revision, BUILD.evidence, BUILD.site]);
  for (const item of argv) {
    const separator = item.indexOf(BUILD.equals);
    const name = item.slice(0, separator);
    if (separator < 0 || !known.has(name) || name in args) {
      if (name === BUILD.benchmarks) throw new Error(ERRORS.mode);
      throw new Error(ERRORS.argument);
    }
    args[name] = item.slice(separator + BUILD.one);
  }
  if (!args[BUILD.isolated] || !args[BUILD.output] || !CONFIG.revisionPattern.test(args[BUILD.revision] ?? BUILD.empty) ||
      !CONFIG.evidencePattern.test(args[BUILD.evidence] ?? BUILD.empty) ||
      !CONFIG.revisionPattern.test(args[BUILD.site] ?? BUILD.empty)) throw new Error(ERRORS.required);
  return args;
}

async function preparePaths(args) {
  const input = resolve(args[BUILD.isolated]);
  if (await realpath(input) !== input || !(await lstat(input)).isDirectory()) throw new Error(ERRORS.symlink);
  const output = await prepareOutputPath(args, input);
  return { input, output };
}

async function prepareIsolated(args, input) {
  const [{ produceIsolatedProjection }, { validateIsolatedCatalog, validateIsolatedProjection }, { readBytes },
    { siteEvidencePlans }] = await Promise.all([
    import('./isolated-projection.mjs'), import('./isolated-loader.mjs'),
    import('../../../scripts/Features/BenchmarkComparisons/aggregate-files.mjs'),
    import('../../../scripts/Features/BenchmarkComparisons/site-isolated-github-contract.mjs')]);
  const manifestPath = join(input, 'aggregate.json');
  const manifest = await readBytes(manifestPath, 4_194_304);
  const projection = await produceIsolatedProjection({ input });
  const plans = siteEvidencePlans(projection.cohort.sourceRevision);
  const legacy = plans.length === 1;
  let qualification;
  if (!legacy) {
    const { validateCompositeSiteEvidence } = await import('../../../scripts/Features/BenchmarkComparisons/composite-site-evidence.mjs');
    qualification = await validateCompositeSiteEvidence({ input, provider: join(dirname(input), 'provider') });
  }
  if (!manifest.equals(await readBytes(manifestPath, 4_194_304))) throw new Error(ERRORS.cohort);
  const bytes = Buffer.from(JSON.stringify(projection) + BUILD.newline);
  if (bytes.length > 4_194_304) throw new Error(ERRORS.budget);
  const catalog = validateIsolatedCatalog({ schemaVersion: 1, generatedAt: new Date().toISOString(),
    siteSourceRevision: args[BUILD.site], measuredSourceRevision: projection.cohort.sourceRevision,
    evidenceUrl: 'https://github.com/managedcode/KeyLoad/actions/runs/' + projection.cohort.runId,
    cohort: projection.cohort, aggregate: { path: 'isolated/aggregate.json', sha256: hash(manifest) },
    projection: { path: 'isolated/projection.json', sha256: hash(bytes) }, rawLocation: 'githubActionsArtifacts' });
  if (args[BUILD.revision] !== catalog.measuredSourceRevision || args[BUILD.evidence] !== catalog.evidenceUrl) {
    throw new Error(ERRORS.cohort);
  }
  validateIsolatedProjection(projection, catalog);
  return { catalog, bytes, manifest, qualification, legacy };
}

function replaceRequired(html, search, replacement) {
  if (!html.includes(search)) throw new Error(ERRORS.html);
  return html.replace(search, replacement);
}

async function emitHtml(output, siteRevision, isolated) {
  let html = await readFile(join(feature, BUILD.html), BUILD.encoding);
  html = html.replaceAll(BUILD.documentationBlob, `/blob/${siteRevision}/docs`)
    .replaceAll(BUILD.documentationTree, `/tree/${siteRevision}/docs`);
  if (!html.includes(BUILD.isolatedEvidence)) throw new Error(ERRORS.html);
  const evidence = `<p><a href="./data/isolated/aggregate.json" download>Native comparison aggregate</a> · ` +
    `<a href="${isolated.catalog.evidenceUrl}">GitHub comparison run</a></p>`;
  const compositeResults = await import('./composite-render.mjs')
    .then(({ renderCompositeResults }) => renderCompositeResults(isolated.qualification));
  html = html.replace(BUILD.isolatedEvidence, evidence + compositeResults);
  await writeFile(join(output, BUILD.html), html);
}

async function emit(output, siteRevision, isolated) {
  await emitHtml(output, siteRevision, isolated);
  await copyAsset(output, BUILD.favicon);
  for (const asset of BUILD.rootAssets) await copyAsset(output, asset);
  for (const [asset, target] of BUILD.metadataCopies) await copyAsset(output, `${BUILD.feature}/${asset}`, target);
  for (const asset of BUILD.commonAssets) await copyAsset(output, `${BUILD.feature}/${asset}`);
  for (const asset of BUILD.measuredAssets) {
    if (asset !== 'composite-render.mjs' || !isolated.legacy) await copyAsset(output, `${BUILD.feature}/${asset}`);
  }
  for (const file of [...BUILD.vendorFiles, BUILD.manifest]) await copyAsset(output, `${BUILD.feature}/${BUILD.vendor}/${file}`);
  const directory = join(output, BUILD.data, 'isolated');
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, 'aggregate.json'), isolated.manifest, { flag: 'wx' });
  await writeFile(join(directory, 'projection.json'), isolated.bytes, { flag: 'wx' });
  const catalog = JSON.stringify(isolated.catalog, null, BUILD.jsonIndent) + BUILD.newline;
  if (Buffer.byteLength(catalog) > 65_536) throw new Error(ERRORS.budget);
  await writeFile(join(output, BUILD.data, 'isolated-catalog.json'), catalog, { flag: 'wx' });
  await writeFile(join(output, BUILD.noJekyll), BUILD.empty);
  await writeFile(join(output, BUILD.cname), BUILD.domain);
  await writeFile(join(output, BUILD.robotsFile), BUILD.robots);
  await writeFile(join(output, BUILD.sitemapFile), BUILD.sitemap);
}

export async function buildMeasuredSite(argv) {
  const args = parseMeasuredArguments(argv);
  const { input, output } = await preparePaths(args);
  const compression = await verifyVendor();
  const sizes = await verifyAssets();
  const isolated = await prepareIsolated(args, input);
  await mkdir(output);
  try {
    await emit(output, args[BUILD.site], isolated);
    return { output, mode: 'measured', benchmarkSource: isolated.catalog.evidenceUrl,
      siteRevision: args[BUILD.site], ...sizes, [BUILD.compression]: compression };
  } catch (error) {
    await rm(output, { recursive: true, force: true });
    throw error;
  }
}
