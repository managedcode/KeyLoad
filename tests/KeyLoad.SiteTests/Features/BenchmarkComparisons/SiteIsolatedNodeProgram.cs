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
          } else {
            throw new Error('Unknown isolated test probe operation.');
          }
          process.stdout.write(JSON.stringify({ ok: true, result }));
        } catch (error) {
          process.stdout.write(JSON.stringify({ ok: false, error: error.code ?? error.name }));
        }
        """;
}
