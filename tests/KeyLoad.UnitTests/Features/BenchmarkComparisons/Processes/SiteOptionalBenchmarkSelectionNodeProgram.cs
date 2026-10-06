namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SiteOptionalBenchmarkSelectionNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        import path from 'node:path';
        let context;
        let contract;
        const assert = condition => { if (!condition) throw new Error('Website admission assertion failed'); };
        """ + SiteWebsiteAdmissionNodeProgram.Source + """
        try {
          const [contextPath, scenario] = process.argv.slice(1);
          context = await import(pathToFileURL(contextPath));
          contract = await import(pathToFileURL(path.join(path.dirname(contextPath), 'site-isolated-github-contract.mjs')));
          let outcome;
          if (scenario.startsWith('website-executor-')) {
            outcome = await assertWebsiteExecutor(scenario);
          } else if (scenario === 'optional-arguments') {
            const base = ['--input=/tmp/keyload-site-capture', '--mode=publish', '--site-revision=' + 'a'.repeat(40),
              '--workflow-revision=' + 'b'.repeat(40)];
            assert(context.parseSiteCaptureArguments([...base, '--optional=true']).optional === 'true');
            assert(context.parseSiteCaptureArguments(base).optional === undefined);
            for (const args of [[...base, '--optional=false'], [...base, '--optional=true', '--mode=validate']]) {
              let rejected = false;
              try { context.parseSiteCaptureArguments(args); }
              catch (error) {
                if (!(error instanceof Error) || error.message !== contract.SITE_GH.failure) throw error;
                rejected = true;
              }
              assert(rejected);
            }
            outcome = 'accepted';
          } else throw new Error('unknown current scenario');
          process.stdout.write(outcome + '\n');
        } catch {
          process.stderr.write('Website admission probe failed.\n');
          process.exitCode = 1;
        }
        """;
}
