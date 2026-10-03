namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubNodeProgram
{
    public const string Source = """
        import { readFile } from 'node:fs/promises';
        import { pathToFileURL } from 'node:url';
        import path from 'node:path';
        const request = JSON.parse(await readFile(process.argv[2], 'utf8'));
        const root = path.join(request.repository, 'scripts/Features/BenchmarkComparisons');
        const load = name => import(pathToFileURL(path.join(root, 'site-isolated-github-' + name + '.mjs')));
        try {
          let result;
          if (request.operation === 'receipt') result = (await load('receipt')).validateSiteIsolatedReceipt(request.receipt);
          else if (request.operation === 'selection') {
            const selection = await (await load('runs')).selectSiteIsolatedEvidence(request.arguments);
            result = selection.state === 'selected' ? {state:selection.state,runId:selection.run.id,
              attempt:selection.run.run_attempt,aggregateJobId:selection.job.id} : selection;
          }
          else if (request.operation === 'proof') {
            const selection = await (await load('runs')).selectSiteIsolatedEvidence(request.arguments);
            result = await (await load('proof')).proveSiteIsolatedEvidence({...request.arguments,selection});
          }
          else if (request.operation === 'inputs') result = await (await load('cli')).runSiteIsolatedGitHub([
            'verify-inputs','--input='+request.arguments.input,'--receipt='+request.arguments.receipt]);
          else if (request.operation === 'fresh') result = await (await load('cli')).runSiteIsolatedGitHub([
            'fresh','--before='+request.arguments.before,'--after='+request.arguments.after]);
          else if (request.operation === 'cli') result = await (await load('cli')).runSiteIsolatedGitHub(request.arguments);
          else throw new Error('Unknown controlled probe operation.');
          process.stdout.write(JSON.stringify({ok:true,result}));
        } catch { process.stdout.write(JSON.stringify({ok:false})); }
        """;
}
