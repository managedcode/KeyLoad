using Aspire.Hosting;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Each full public exchange, including response decoding, has its own explicit cancellation bound.</summary>
internal static class IsolatedKeyLoadFaultRegressionCalls
{
    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(IsolatedKeyLoadFaultRegressionProtocol.AttemptSeconds, token);
        var value = await call(attempt.Token);
        attempt.Token.ThrowIfCancellationRequested();
        return value;
    }

    internal static async Task RunAsync(Func<CancellationToken, Task> call, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(IsolatedKeyLoadFaultRegressionProtocol.AttemptSeconds, token);
        await call(attempt.Token);
        attempt.Token.ThrowIfCancellationRequested();
    }

    internal static async Task<IsolatedKeyLoadPublicRegressionMcp> ConnectAsync(DistributedApplication app,
        int node, string key, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(IsolatedKeyLoadFaultRegressionProtocol.AttemptSeconds, token);
        var native = await IsolatedKeyLoadPublicRegressionMcp.ConnectAsync(app, node, key, attempt.Token);
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
