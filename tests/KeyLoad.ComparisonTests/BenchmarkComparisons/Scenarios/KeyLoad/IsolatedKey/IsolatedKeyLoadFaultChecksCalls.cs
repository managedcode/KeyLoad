using Aspire.Hosting;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Each full public exchange, including response decoding, has its own explicit cancellation bound.</summary>
internal static class IsolatedKeyLoadFaultChecksCalls
{
    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token);
        var value = await call(attempt.Token);
        attempt.Token.ThrowIfCancellationRequested();
        return value;
    }

    internal static async Task RunAsync(Func<CancellationToken, Task> call, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token);
        await call(attempt.Token);
        attempt.Token.ThrowIfCancellationRequested();
    }

    internal static async Task<IsolatedKeyLoadPublicChecksMcp> ConnectAsync(DistributedApplication app,
        int node, string key, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token);
        var native = await IsolatedKeyLoadPublicChecksMcp.ConnectAsync(app, node, key, attempt.Token);
        try
        {
            attempt.Token.ThrowIfCancellationRequested();
            return native;
        }
        catch (Exception)
        {
            await native.DisposeAsync();
            throw;
        }
    }
}
