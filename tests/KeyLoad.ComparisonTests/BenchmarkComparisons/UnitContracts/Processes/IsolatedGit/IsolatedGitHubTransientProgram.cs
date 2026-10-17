namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubTransientProgram
{
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { readFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, input, originalPath] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const parser = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-headers.mjs')));
        const original = await readFile(originalPath);
        const now = Date.parse('2026-10-03T21:47:29Z');
        const accepted = new Set(['original', 'internal-error', 'bad-gateway', 'gateway-timeout',
          'second-retry', 'third-retry', 'retry-after', 'retry-date', 'provider-limits']).has(input);
        try {
          let response = parser.parseResponseHeaders(original);
          assert.equal(response.status, 503);
          let repeat = input === 'second-retry' ? 1 : input === 'third-retry' ? 2 : 0;
          let waited = 0;
          let expected = [5000 * 2 ** repeat, 5000 * 2 ** repeat + 1000];
          const statuses = { 'internal-error': 500, 'bad-gateway': 502, 'gateway-timeout': 504,
            unauthorized: 401, forbidden: 403, 'not-found': 404, 'unsupported-status': 408,
            'successful-response': 200 };
          if (Object.hasOwn(statuses, input)) response.status = statuses[input];
          if (input === 'retry-after') {
            response.headers['retry-after'] = '15'; expected = [16000, 16000];
          }
          if (input === 'retry-date') {
            response.headers['retry-after'] = 'Sat, 03 Oct 2026 21:47:41 GMT'; expected = [13000, 13000];
          }
          if (input === 'provider-limits') {
            response.headers['retry-after'] = '90';
            response.headers['x-ratelimit-remaining'] = '0';
            response.headers['x-ratelimit-reset'] = String(now / 1000 + 60); expected = [91000, 91000];
          }
          if (input === 'invalid-retry') response.headers['retry-after'] = 'invalid';
          if (input === 'invalid-reset') {
            response.headers['x-ratelimit-remaining'] = '0'; response.headers['x-ratelimit-reset'] = 'invalid';
          }
          if (input === 'duplicate-header') response = parser.parseResponseHeaders(Buffer.from(
            'HTTP/2.0 503 Service Unavailable\nRetry-After: 15\r\nRetry-After: 20\r\n\r\n'));
          if (input === 'missing-headers') response.headers = null;
          if (input === 'array-headers') response.headers = [];
          if (input === 'wait-budget') waited = 3700000 - 4999;
          if (input === 'repeat-budget') repeat = 3;
          if (input === 'negative-wait') waited = -1;
          if (input === 'negative-repeat') repeat = -1;
          if (input === 'fractional-repeat') repeat = 0.5;
          const delay = api.retryDelay(response, now, waited, repeat);
          if (accepted) {
            assert.ok(delay >= expected[0] && delay <= expected[1]);
            assert.ok(waited + delay <= 3700000);
          }
          process.stdout.write('accepted\n');
        } catch { process.stdout.write('rejected\n'); process.exitCode = 1; }
        """;
}
