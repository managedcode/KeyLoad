namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SiteOptionalBenchmarkSelectionNodeProgram
{
    internal const string Source = """
        import { readFile } from 'node:fs/promises';
        import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
        import { createHash } from 'node:crypto';
        import os from 'node:os';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, fixtures, scenario] = process.argv.slice(1);
        const runs = await import(pathToFileURL(modulePath));
        const contract = await import(pathToFileURL(path.join(path.dirname(modulePath), 'site-isolated-github-contract.mjs')));
        const context = await import(pathToFileURL(path.join(path.dirname(modulePath), 'site-isolated-github-context.mjs')));
        const proof = await import(pathToFileURL(path.join(path.dirname(modulePath), 'aggregate-proof.mjs')));
        const transport = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-transport.mjs')));
        const freshness = await import(pathToFileURL(path.join(path.dirname(modulePath), 'site-isolated-github-fresh.mjs')));
        const read = async name => JSON.parse(await readFile(path.join(fixtures, name), 'utf8'));
        const readyRun = await read('SiteOptionalRetainedReadyRun.json');
        const workflow = await read('SiteOptionalRetainedWorkflow.json');
        const readyJob = await read('SiteOptionalRetainedReadyAggregate.json');
        const unavailableRun = await read('SiteOptionalRetainedUnreadyRun.json');
        const unavailableJob = await read('SiteOptionalRetainedUnreadyAggregate.json');
        const assert = condition => { if (!condition) throw new Error('selection assertion failed'); };
        try {
          if (scenario === 'retained-success') {
            const result = runs.selectSiteAggregateJob(readyRun, [readyJob]);
            assert(result.successful && result.job.id === 111403286924);
            assert(contract.siteAggregateSteps(readyRun.head_sha).join('|') === contract.SITE_GH.legacySteps.join('|'));
          } else if (scenario === 'retained-unready') {
            const result = runs.selectSiteAggregateJob(unavailableRun, [unavailableJob]);
            assert(!result.successful && result.run.id === unavailableRun.id);
          } else if (scenario === 'failed-aggregate') {
            const failed = { ...readyJob, status: 'completed', conclusion: 'failure' };
            assert(!runs.selectSiteAggregateJob(readyRun, [failed]).successful);
          } else if (scenario === 'no-aggregate') {
            assert(!runs.selectSiteAggregateJob(readyRun, []).successful);
          } else if (scenario === 'current-success') {
            const modernRun = { ...readyRun, head_sha: 'b'.repeat(40) };
            const modernJob = { ...readyJob, head_sha: modernRun.head_sha,
              steps: contract.SITE_GH.steps.map((name, index) => ({ name, status: 'completed', conclusion: 'success', number: index + 1 })) };
            const result = runs.selectSiteAggregateJob(modernRun, [modernJob]);
            assert(result.successful && result.job.steps.some(step => step.name === 'Check control and complete scale accounting'));
          } else if (scenario === 'invalid-latest-ready') {
            const modernRun = { ...readyRun, head_sha: 'c'.repeat(40) };
            const modernJob = { ...readyJob, head_sha: modernRun.head_sha,
              steps: contract.SITE_GH.steps.slice(0, -1).map((name, index) => ({ name, status: 'completed', conclusion: 'success', number: index + 1 })) };
            let rejected = false; try { runs.selectSiteAggregateJob(modernRun, [modernJob]); } catch { rejected = true; }
            assert(rejected);
          } else if (scenario === 'ordered-producers') {
            const result = runs.listSiteProducers([readyRun, unavailableRun], workflow);
            assert(result.length === 2 && result[0].id === unavailableRun.id && result[1].id === readyRun.id);
          } else if (scenario === 'empty-workflow-identity') {
            runs.validateSiteWorkflow(workflow);
            assert(runs.listSiteProducers([], workflow).length === 0);
            let rejected = false; try { runs.validateSiteWorkflow({ ...workflow, path: '.github/workflows/other.yml' }); }
            catch { rejected = true; }
            assert(rejected);
          } else if (scenario === 'source-contract-routes') {
            const historical = await import(pathToFileURL(path.join(path.dirname(modulePath), 'historical-isolated-plan.mjs')));
            for (const revision of historical.HISTORICAL.sourceRevisions) {
              assert(transport.isAllowedGitHubEndpoint(`repos/managedcode/KeyLoad/contents/${historical.HISTORICAL.path}?ref=${revision}`));
            }
            assert(transport.isAllowedGitHubEndpoint('repos/managedcode/KeyLoad/actions/runs/37184989107'));
            for (const route of [
              `repos/managedcode/KeyLoad/contents/${historical.HISTORICAL.path}?ref=${'d'.repeat(40)}`,
              `repos/other/KeyLoad/contents/${historical.HISTORICAL.path}?ref=${readyRun.head_sha}`,
              `repos/managedcode/KeyLoad/contents/other.json?ref=${readyRun.head_sha}`,
              `repos/managedcode/KeyLoad/contents/${historical.HISTORICAL.path}?ref=${readyRun.head_sha}&path=other`,
            ]) assert(!transport.isAllowedGitHubEndpoint(route));
          } else if (scenario === 'optional-arguments') {
            const base = ['--input=/tmp/keyload-site-capture', '--mode=publish', '--site-revision=' + 'a'.repeat(40),
              '--workflow-revision=' + 'b'.repeat(40)];
            assert(context.parseSiteCaptureArguments([...base, '--optional=true']).optional === 'true');
            assert(context.parseSiteCaptureArguments(base).optional === undefined);
            for (const args of [[...base, '--optional=false'], [...base, '--optional=true', '--mode=validate']]) {
              let rejected = false; try { context.parseSiteCaptureArguments(args); } catch { rejected = true; }
              assert(rejected);
            }
          } else if (scenario.startsWith('freshness-')) {
            const temporary = await mkdtemp(path.join(os.tmpdir(), 'keyload-site-optional-'));
            const root = await realpath(temporary);
            try {
              const makeSnapshot = async (name, source) => {
                const directory = path.join(root, name);
                await mkdir(path.join(directory, 'metadata'), { recursive: true });
                const entries = [
                  ['run.json', await readFile(path.join(fixtures, 'SiteOptionalRetainedUnreadyRun.json'))],
                  ['aggregate.json', await readFile(path.join(fixtures, 'SiteOptionalRetainedUnreadyAggregate.json'))],
                ];
                const metadataFiles = [];
                for (const [file, bytes] of entries) {
                  const relative = 'metadata/' + file;
                  await writeFile(path.join(directory, relative), bytes);
                  metadataFiles.push({ path: relative, bytes: bytes.length,
                    sha256: createHash('sha256').update(bytes).digest('hex') });
                }
                const receipt = { schemaVersion: 1, state: 'unavailable', mode: 'publish', publishEligible: true,
                  source, producer: null, metadataFiles };
                const receiptPath = path.join(directory, 'metadata-proof.json');
                await writeFile(receiptPath, JSON.stringify(receipt));
                return { directory, receiptPath, receipt };
              };
              const source = { website: 'a'.repeat(40), control: 'b'.repeat(40) };
              const before = await makeSnapshot('before', source);
              const afterSource = scenario === 'freshness-source-change' ? { ...source, website: 'c'.repeat(40) } : source;
              const after = await makeSnapshot('after', afterSource);
              if (scenario === 'freshness-null-to-ready' || scenario === 'freshness-ready-to-null') {
                const readyPath = path.join(root, 'ready.json');
                await writeFile(readyPath, JSON.stringify({ state: 'metadata_verified' }));
                let rejected = false;
                try { await freshness.verifySiteIsolatedFreshness(scenario === 'freshness-null-to-ready'
                  ? { before: before.receiptPath, after: readyPath } : { before: readyPath, after: after.receiptPath }); }
                catch { rejected = true; }
                assert(rejected);
              } else if (scenario === 'freshness-tamper') {
                await writeFile(path.join(after.directory, 'metadata', 'aggregate.json'), 'changed');
                let rejected = false; try { await freshness.verifySiteIsolatedFreshness({ before: before.receiptPath, after: after.receiptPath }); }
                catch { rejected = true; }
                assert(rejected);
              } else if (scenario === 'freshness-source-change') {
                let rejected = false; try { await freshness.verifySiteIsolatedFreshness({ before: before.receiptPath, after: after.receiptPath }); }
                catch { rejected = true; }
                assert(rejected);
              } else {
                const result = await freshness.verifySiteIsolatedFreshness({ before: before.receiptPath, after: after.receiptPath });
                assert(result.fresh && result.state === 'unavailable' && result.producer === null);
              }
            } finally { await rm(temporary, { recursive: true, force: true }); }
          } else if (scenario === 'legacy-plan') {
            const plan = contract.siteEvidencePlans(readyRun.head_sha);
            assert(plan.length === 1 && plan[0].cells.length === 270 && plan[0].workerSchemaVersion === 4);
            assert(contract.siteEvidencePlans('d'.repeat(40)).reduce((count, item) => count + item.cells.length, 0) === 1386);
          } else if (scenario === 'legacy-plan-reject-changed') {
            const plan = contract.siteEvidencePlans(readyRun.head_sha)[0];
            let rejected = false;
            try { proof.validateAggregateProof({ cohort: { sourceRevision: readyRun.head_sha } }, { ...plan, cells: plan.cells.slice(1) }); }
            catch { rejected = true; }
            assert(rejected);
          } else if (scenario === 'legacy-plan-reject-source') {
            const plan = contract.siteEvidencePlans(readyRun.head_sha)[0];
            let rejected = false;
            try { proof.validateAggregateProof({ cohort: { sourceRevision: 'd'.repeat(40) } }, plan); }
            catch { rejected = true; }
            assert(rejected);
          } else throw new Error('unknown scenario');
          process.stdout.write('accepted\n');
        } catch {
          process.stdout.write('rejected\n');
          process.exitCode = 1;
        }
        """;
}
