namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ImageHttpDeadlineNodeProgram
{
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { pathToFileURL } from 'node:url';
        const [modulePath, scenario] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        if (scenario === 'abort') {
          let reason;
          let operationTerminal = false;
          const result = await api.withHttpDeadline(100, signal => new Promise(resolve => {
            signal.addEventListener('abort', () => {
              reason = signal.reason;
              setTimeout(() => { operationTerminal = true; resolve(signal.reason); }, 50);
            }, { once: true });
          }));
          assert.equal(operationTerminal, true);
          assert.equal(result, reason);
          assert.equal(reason.name, 'TimeoutError');
          process.stdout.write('aborted\n');
        } else if (scenario === 'success') {
          let signal;
          const result = await api.withHttpDeadline(30000, value => { signal = value; return 42; });
          assert.equal(result, 42);
          assert.equal(signal.aborted, false);
          process.stdout.write('success:42\n');
        } else if (scenario === 'sync-failure' || scenario === 'async-failure') {
          const original = new Error('original operation failure');
          let observed;
          try {
            await api.withHttpDeadline(30000, () => {
              if (scenario === 'sync-failure') throw original;
              return Promise.reject(original);
            });
          } catch (error) { observed = error; }
          assert.equal(observed, original);
          process.stdout.write(scenario === 'sync-failure' ? 'sync-error-preserved\n' : 'async-error-preserved\n');
        } else if (scenario === 'late-abort') {
          let signal;
          let abortEvents = 0;
          await api.withHttpDeadline(100, value => {
            signal = value;
            signal.addEventListener('abort', () => abortEvents++, { once: true });
            return 'done';
          });
          await new Promise(resolve => setTimeout(resolve, 250));
          assert.equal(signal.aborted, false);
          assert.equal(abortEvents, 0);
          process.stdout.write('no-late-abort\n');
        } else if (scenario === 'invalid') {
          let calls = 0;
          const bounds = [0, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY, 30001];
          for (const timeoutMs of bounds) {
            let rejected = false;
            try { await api.withHttpDeadline(timeoutMs, () => { calls++; }); }
            catch { rejected = true; }
            assert.equal(rejected, true);
          }
          assert.equal(calls, 0);
          process.stdout.write('invalid-bounds-rejected\n');
        } else {
          throw new Error('Unknown test scenario.');
        }
        """;
}
