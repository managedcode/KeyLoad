namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedNodeProgram
{
    // This probe invokes production code over actual files. Corrupt copies are parser inputs only.
    public const string Source = """
        import { readFile, writeFile } from 'node:fs/promises';
        import { pathToFileURL } from 'node:url';
        import { join } from 'node:path';
        const request = JSON.parse(await readFile(process.argv[2], 'utf8'));
        const module = name => import(pathToFileURL(join(request.repository,
          'site/Features/BenchmarkComparisons', 'isolated-' + name + '.mjs')));
        function corruptOriginal(value, corruption) {
          if (corruption === null) return;
          const report = value.report, item = report.cases[0], measurement = item.measurement;
          switch (corruption) {
            case 'missingCase': report.cases.pop(); break;
            case 'duplicateRepetition': report.cases[1].repetition = item.repetition; break;
            case 'caseStatus': item.status = 'failed'; break;
            case 'dataset': report.datasetSha256 = '0'.repeat(64); break;
            case 'provenance': report.provenance.attempt += 1; break;
            case 'topology': report.targets[0].cluster.nodes += 1; break;
            case 'copies': report.targets[0].cluster.dataCopies += 1; break;
            case 'host': report.hostOs = 'Windows'; break;
            case 'options': report.options.operations = 1; break;
            case 'percentiles': measurement.latency.p99Ms = -1; break;
            case 'order': measurement.latency.p50Ms = measurement.latency.p99Ms + 1; break;
            case 'throughput': measurement.usefulOperationsPerSecond = 0; break;
            case 'failures': measurement.failures = 1; break;
            case 'resources': measurement.clientResources.samplingIntervalMs = 0; break;
            case 'queueCompleted': measurement.uniqueCompletedMessages -= 1; break;
            case 'queueEnqueue': measurement.enqueue = null; break;
            case 'queueReceive': measurement.receive.p50Ms = measurement.receive.p99Ms + 1; break;
            case 'queueAck': measurement.ack = null; break;
            case 'samples': item.samples = []; break;
            case 'nullCorpus': value.datasetSha256 = null; break;
            case 'reportNull': value.report = null; break;
            case 'detail': item.detail = 'untrusted diagnostic'; break;
            case 'target': report.targets[0].name = 'foreign'; break;
            case 'run': report.runId = 'invalid'; break;
            case 'schema': report.schemaVersion = 0; break;
            default: throw new Error('Unknown original report corruption.');
          }
        }
        try {
          let result;
          if (request.operation === 'produce') {
            const { produceIsolatedProjection } = await module('projection');
            const value = await produceIsolatedProjection({ input: request.input });
            await writeFile(request.output, JSON.stringify(value), { flag: 'wx' });
            result = { workers: value.workers.length };
          } else if (request.operation === 'validate') {
            const { validateIsolatedCatalog, validateIsolatedProjection } = await module('loader');
            const catalog = JSON.parse(await readFile(request.catalog, 'utf8'));
            validateIsolatedCatalog(catalog);
            const projection = JSON.parse(await readFile(request.projection, 'utf8'));
            result = validateIsolatedProjection(projection, catalog).workers.length;
          } else if (request.operation === 'rows') {
            const { selectedIsolatedRows } = await module('measurements');
            const projection = JSON.parse(await readFile(request.projection, 'utf8'));
            result = selectedIsolatedRows(projection, request.scenario, request.nodeCount,
              request.repetition, request.metric, request.target);
          } else if (request.operation === 'load') {
            const { loadIsolatedCatalog, loadIsolatedProjection } = await module('loader');
            const controller = new AbortController();
            if (request.abort) controller.abort();
            const entry = await loadIsolatedCatalog({ catalogUrl: request.catalogUrl, signal: controller.signal });
            result = (await loadIsolatedProjection({ entry, baseUrl: request.baseUrl, signal: controller.signal })).workers.length;
          } else if (request.operation === 'rowsBatch') {
            const { selectedIsolatedRows } = await module('measurements');
            const projection = JSON.parse(await readFile(request.projection, 'utf8'));
            const selections = [];
            for (const selection of request.selections) {
              const rows = selectedIsolatedRows(projection, selection.scenario, selection.nodeCount,
                selection.repetition, selection.metric, selection.target);
              selections.push({ selection, rows: rows.map(({ worker, ...row }) => ({ ...row,
                workerId: worker.id, jobId: worker.job.id, artifactId: worker.artifact.id })) });
            }
            await writeFile(request.output, JSON.stringify(selections), { flag: 'wx' });
            result = selections.length;
          } else if (request.operation === 'originalReport') {
            const value = JSON.parse(await readFile(request.fixture, 'utf8'));
            corruptOriginal(value, request.corruption);
            const { validateCompactReport } = await module('report-validation');
            validateCompactReport(value.report, value.worker, value.cohort, value.datasetSha256);
            const { selectedRows } = await import(pathToFileURL(join(request.repository,
              'site/Features/BenchmarkComparisons/measurements.mjs')));
            const row = selectedRows(value.report, value.worker.scenario, request.repetition, request.metric)[0];
            result = { sourceRevision: value.report.sourceRevision, provenance: value.report.provenance, row };
          } else {
            throw new Error('Unknown isolated test probe operation.');
          }
          process.stdout.write(JSON.stringify({ ok: true, result }));
        } catch (error) {
          process.stdout.write(JSON.stringify({ ok: false, error: error.code ?? error.name }));
        }
        """;
}
