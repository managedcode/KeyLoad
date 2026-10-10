using System.Text;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Log
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, Guid actualCommandId, CancellationToken token)
    {
        var logs = wave.Application.Services.GetRequiredService<ResourceLoggerService>();
        var expected = SampleChunkPendingRf3Protocol.RefusalPrefix
            + actualCommandId.ToString(SampleChunkPendingRf3Protocol.GuidFormat)
            + SampleChunkPendingRf3Protocol.RefusalSuffix;
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
        await RequireOwnedAsync(logs, expected, owned, token).ConfigureAwait(false);
    }

    private static async Task RequireOwnedAsync(ResourceLoggerService logs, string expected,
        CancellationTokenSource owned, CancellationToken token)
    {
        var ownedToken = owned.Token;
        var readers = RequestCqrsProbeFixtureProtocol.Nodes.Select(node => ReadAsync(logs, node, expected, ownedToken)).ToArray();
        var failures = new List<Exception>();
        string? original = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var first = await Task.WhenAny(readers).WaitAsync(token).ConfigureAwait(false);
            original = await first.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (original is null)
            { throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(owned.Cancel, failures);
        await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(readers), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await TestContext.Current!.OutputWriter.WriteLineAsync(original);
    }

    private static async Task<string?> ReadAsync(ResourceLoggerService logs, string node, string expected,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        var reader = logs.WatchAsync(node).GetAsyncEnumerator(token);
        string? observed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                while (await reader.MoveNextAsync().ConfigureAwait(false))
                {
                    observed = FindMatch(reader.Current, expected);
                    if (observed is not null)
                    { return; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => reader.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return observed;
    }

    private static string? FindMatch(IEnumerable<LogLine> actualBatch, string expected)
    {
        foreach (var line in actualBatch)
        {
            if (!line.Content.Contains(expected, StringComparison.Ordinal))
            { continue; }
            if (Encoding.UTF8.GetByteCount(line.Content) > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded); }
            return line.Content;
        }
        return null;
    }
}
