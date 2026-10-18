namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubRateProgram
{
    internal const string Source = """
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, input] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const parser = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-headers.mjs')));
        const now = Date.parse('2026-10-03T10:00:00Z');
        if (input.startsWith('native-')) {
          // Original masked public run38878701598 artifact12037090476 requests32/33/34; classifier inputs only, never publication authority.
        """
        + "\n" + IsolatedGitHubNativeTransportReceipts.Source + "\n" +
        """
          const record = input === 'native-recorded-success' ? recorded.recovered
            : input === 'native-recorded-502' ? recorded.transient
            : input === 'native-recorded-partial' ? recorded.partial : recorded.failure;
          const result = { response: { status: record.status, headers: { 'x-ratelimit-remaining': '3714' } },
            nativeProcess: structuredClone(record.nativeProcess), bytes: record.bytes };
          let stderr = Buffer.from(input === 'native-recorded-success' ? ''
            : input === 'native-recorded-502' ? 'gh: Server Error (HTTP 502)\n'
            : 'stream error: stream ID 1; CANCEL; received from peer\n');
          if (input === 'native-exit-success') result.nativeProcess.exitCode = 0;
          if (input === 'native-partial-body') result.bytes = 1;
          if (input === 'native-negative-body') result.bytes = -1;
          if (input === 'native-oversized-body') result.bytes = 16777217;
          if (input === 'native-unsafe-body') result.bytes = Number.MAX_SAFE_INTEGER + 1;
          if (input === 'native-auth') result.response.status = 401;
          if (input === 'native-timeout') result.nativeProcess.timedOut = true;
          if (input === 'native-overflow') result.nativeProcess.exceeded = true;
          if (input === 'native-spawn-failure') result.nativeProcess.spawnFailed = true;
          if (input === 'native-cancelled') result.nativeProcess.cancelled = true;
          if (input === 'native-signal') result.nativeProcess.signal = 'SIGTERM';
          if (input === 'native-wrong-hash') result.nativeProcess.stderrSha256 = '0'.repeat(64);
          if (input === 'native-wrong-error' || input === 'native-complete-invalid-json') {
            const { createHash } = await import('node:crypto');
            stderr = Buffer.from(input === 'native-wrong-error' ? 'invalid complete provider data\n' : '');
            result.nativeProcess.stderrBytes = stderr.length;
            result.nativeProcess.stderrSha256 = createHash('sha256').update(stderr).digest('hex');
            if (input === 'native-complete-invalid-json') {
              result.nativeProcess.exitCode = 0;
              result.bytes = Buffer.byteLength('{');
            }
          }
          const original = JSON.stringify({ result, stderr: stderr.toString('hex') });
          try {
            const interrupted = api.isInterruptedMetadataResponse(result, stderr);
            if (input === 'native-recorded-success') {
              if (interrupted || result.response.status !== 200 || result.nativeProcess.exitCode !== 0) throw new Error('healthy');
            } else {
              const delay = api.retryDelay(result.response, now, input === 'native-wait-budget' ? 3700000 : 0,
                input === 'native-repeat-budget' ? 3 : 0, interrupted);
              if (delay < 5000 || delay > 6000) throw new Error('delay');
            }
            const transport = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-transport.mjs')));
            const args = transport.nativeArguments(record.endpoint, { transport: { allowBinary: false } });
            const expected = ['api', '--hostname', 'github.com', '--method', 'GET', '--include', '-H',
              'Accept: application/vnd.github+json', '-H', 'X-GitHub-Api-Version: 2022-11-28', record.endpoint];
            if (JSON.stringify(args) !== JSON.stringify(expected)
              || JSON.stringify({ result, stderr: stderr.toString('hex') }) !== original) throw new Error('changed');
            process.stdout.write('accepted\n');
          } catch { process.stdout.write('rejected\n'); process.exitCode = 1; }
        } else {
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
        }
        """;
}
