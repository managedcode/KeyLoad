const escape = value => String(value).replace(/[&<>"']/gu, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
const number = value => Number.isFinite(value) ? new Intl.NumberFormat('en', { maximumFractionDigits: 3 }).format(value) : 'Unavailable';
const memory = value => Number.isFinite(value) ? number(value / 1_048_576) + ' MiB' : 'Unavailable';

function provenance(cell) {
  return `<a href="${escape(cell.job.url)}">${cell.disposition === 'failed' ? 'Failed job' : 'Worker job'}</a> · ` +
    `<a href="${escape(cell.job.url.replace(/\/job\/\d+$/u, '') + '/artifacts/' + cell.artifact.id)}">Original report and server samples</a>`;
}

function row(cell, vector) {
  const measured = cell.disposition === 'measured';
  const metrics = measured ? cell.vectorMetrics : null;
  const data = measured ? cell.measurement : null;
  const values = [cell.target, cell.nodeCount, cell.scenario,
    measured ? 'Measured' : cell.reason, number(vector ? metrics?.queryUsefulOperationsPerSecond : data?.usefulOperationsPerSecond),
    number(vector ? metrics?.latencyP95Ms : data?.latency?.p95Ms), number(vector ? metrics?.latencyP99Ms : data?.latency?.p99Ms),
    ...(vector ? [number(metrics?.exactRecall), number(metrics?.minimumRecall), number(metrics?.indexBuildMilliseconds)] : []),
    memory(measured ? cell.serverMemoryBytes : null)];
  const target = cell.nativeTarget;
  const contract = target ? `${target.writeAcknowledgement}; ${target.readContract}; ${target.cluster.observations.join('; ')}` : cell.reason;
  return `<tr data-worker-id="${escape(cell.id)}">${values.map(value => `<td>${escape(value)}</td>`).join('')}` +
    `<td>${provenance(cell)}<details><summary>Native contract</summary><p>${escape(contract)}</p>` +
    (metrics ? `<p>Index: ${escape(metrics.nativeIndexDefinition)}. Query plan: ${escape(metrics.nativeQueryPlan)}. Parameters: ${escape(JSON.stringify(metrics.indexParameters))}.</p>` : '') +
    (cell.resourceEquivalence ? `<p>Effective server resources: ${escape(JSON.stringify(cell.resourceEquivalence))}.</p>` : '') +
    `<p>Worker SHA256 ${escape(cell.rawSha256)}. Artifact ${escape(cell.artifact.id)} (${escape(cell.artifact.digest)}). Resource contract ${cell.resourceQualified ? 'verified' : 'unavailable'}.</p></details></td></tr>`;
}

function profile(profile, vector) {
  const settings = profile.settings;
  const headers = ['Database', 'Native nodes', 'Operation', 'Status', 'Useful operations/s', 'p95 ms (sample estimate)', 'p99 ms (sample estimate)',
    ...(vector ? ['Mean recall@10', 'Minimum recall@10', 'Index build ms'] : []), 'Observed server RAM', 'GitHub results'];
  const description = vector ? `${settings.recordCount.toLocaleString('en')} records; ${settings.indexKind}; ${settings.queryMode}; cosine; ` +
    `${settings.measuredQueries.toLocaleString('en')} measured queries; ${settings.updateCount.toLocaleString('en')} updates; concurrency ${settings.concurrency}.`
    : `${settings.documents.toLocaleString('en')} records; ${settings.operations.toLocaleString('en')} measured operations; concurrency ${settings.concurrency}.`;
  return `<details data-profile="${escape(profile.id)}"><summary>${escape(profile.id)}</summary><p>${escape(description)}</p>` +
    `<p>Corpus SHA256 ${escape(profile.datasetSha256 ?? 'unavailable')}. RAM is the sum of observed per-node RSS maxima over the whole run, sampled every 5 seconds; these maxima need not occur together. It is not a peak or an index/query phase measurement.</p>` +
    `<div class="table-scroll"><table><caption>${escape(profile.id)} · compare matching native topology and acknowledgement contracts</caption><thead><tr>` +
    headers.map(header => `<th scope="col">${escape(header)}</th>`).join('') + '</tr></thead><tbody>' +
    profile.cells.map(cell => row(cell, vector)).join('') + '</tbody></table></div></details>';
}

export function renderCompositeResults(value) {
  if (value === null || value === undefined) return '<section id="scale-benchmarks"><h2>Native database scale tests</h2><p>The recorded historical run contains the small control workload. Measurements for 100,000 and 1,000,000 record document and vector profiles are unavailable in this run.</p></section>';
  return '<section id="scale-benchmarks" aria-labelledby="scale-benchmarks-title"><h2 id="scale-benchmarks-title">Native database scale tests</h2>' +
    '<p>100,000 and 1,000,000 actual records. Independent Linux jobs use the same corpus, workload and resource contract. Failed or unsupported cells have no measurements. Match native nodes and acknowledgement/read contracts before comparing values.</p>' +
    '<h3>Document operations</h3>' + value.scaledProfiles.map(item => profile(item, false)).join('') +
    '<h3>Vector search, accuracy and index costs</h3>' + value.vectorProfiles.map(item => profile(item, true)).join('') + '</section>';
}
