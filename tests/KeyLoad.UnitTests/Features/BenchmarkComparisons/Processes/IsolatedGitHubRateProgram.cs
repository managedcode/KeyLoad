namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubRateProgram
{
    internal const string Source = """
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, input] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const parser = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-headers.mjs')));
        const now = Date.parse('2026-10-03T10:00:00Z');
        let headers = 'X-RateLimit-Remaining: 0\r\nX-RateLimit-Reset: ' + (now / 1000 + 60) + '\r\n';
        let status = 429;
        if (input === 'retry-after') headers = 'Retry-After: 90\r\n';
        if (input === 'both') headers += 'Retry-After: 90\r\n';
        if (input === 'unauthorized') status = 401;
        if (input === 'absent') headers = '';
        if (input === 'remaining') headers = headers.replace('Remaining: 0', 'Remaining: 1');
        if (input === 'invalid-reset') headers = headers.replace(String(now / 1000 + 60), 'invalid');
        if (input === 'invalid-retry') headers = 'Retry-After: invalid\r\n';
        if (input === 'duplicate-header') headers += 'X-RateLimit-Remaining: 0\r\n';
        try {
          const response = parser.parseResponseHeaders(Buffer.from('HTTP/2.0 ' + status + ' Provider\n' + headers + '\r\n'));
          const delay = api.retryDelay(response, now, input === 'wait-budget' ? 3690000 : 0, input === 'repeat-budget' ? 3 : 0);
          if (delay < (input === 'reset' ? 60000 : 90000) || delay > 100000) throw new Error('delay');
          process.stdout.write('accepted\n');
        } catch { process.stdout.write('rejected\n'); process.exitCode = 1; }
        """;
}
