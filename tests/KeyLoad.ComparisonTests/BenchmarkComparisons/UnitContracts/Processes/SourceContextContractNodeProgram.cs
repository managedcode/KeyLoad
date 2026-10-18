namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class SourceContextContractNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        const inputs = await import(pathToFileURL(process.argv[1]));
        const request = JSON.parse(Buffer.from(process.argv[2], 'base64').toString('utf8'));
        try {
          const context = inputs.createRunContext(request.environment, request.platform);
          let value;
          if (request.operation === 'context')
            value = { sourceSha: context.sourceSha, runId: context.runId, runAttempt: context.runAttempt,
              repository: context.repository, ref: context.ref };
          else if (request.operation === 'source') {
            inputs.assertCleanSourceRevision(context, request.head, request.diffExitCode, request.status);
            value = true;
          } else throw new Error('Unsupported source contract operation.');
          process.stdout.write(JSON.stringify({ ok: true, value }) + '\n');
        } catch (error) {
          const message = error instanceof Error ? error.message : 'Source contract input was rejected.';
          process.stdout.write(JSON.stringify({ ok: false, message }) + '\n');
        }
        """;
}
