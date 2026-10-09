using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkNativeJobRf3Log
{
    internal static async Task<SampleChunkNativeJobRf3Receipt> ReturnedAsync(TwoRf3MembershipWave wave,
        Guid commandId, CancellationToken token)
        => await ReadAsync(wave, commandId, false, token).ConfigureAwait(false);

    internal static async Task RequireExecutingAsync(TwoRf3MembershipWave wave,
        SampleChunkNativeJobRf3Receipt original, CancellationToken token)
    {
        var actual = await ReadAsync(wave, original.CommandId, true, token).ConfigureAwait(false);
        await Assert.That(actual).IsEqualTo(original);
    }

    private static async Task<SampleChunkNativeJobRf3Receipt> ReadAsync(TwoRf3MembershipWave wave,
        Guid commandId, bool executing, CancellationToken token)
    {
        var logs = wave.Application.Services.GetRequiredService<ResourceLoggerService>();
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
        return await ReadOwnedAsync(logs, commandId, executing, owned, token).ConfigureAwait(false);
    }

    private static async Task<SampleChunkNativeJobRf3Receipt> ReadOwnedAsync(ResourceLoggerService logs,
        Guid commandId, bool executing, CancellationTokenSource owned, CancellationToken token)
    {
        var ownedToken = owned.Token;
        var readers = TwoRf3MembershipProtocol.Nodes
            .Select(node => ReadNodeAsync(logs, node, commandId, executing, ownedToken)).ToArray();
        var failures = new List<Exception>();
        SampleChunkNativeJobRf3Receipt? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var first = await Task.WhenAny(readers).WaitAsync(token).ConfigureAwait(false);
            receipt = await first.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (receipt is null) { throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(owned.Cancel, failures);
        await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(readers), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return receipt ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing);
    }

    private static async Task<SampleChunkNativeJobRf3Receipt?> ReadNodeAsync(ResourceLoggerService logs,
        string node, Guid commandId, bool executing, CancellationToken token)
    {
        var failures = new List<Exception>();
        var reader = logs.WatchAsync(node).GetAsyncEnumerator(token);
        SampleChunkNativeJobRf3Receipt? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                while (await reader.MoveNextAsync().ConfigureAwait(false))
                {
                    receipt = FindMatch(reader.Current, commandId, executing);
                    if (receipt is not null) { return; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => reader.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return receipt;
    }

    private static SampleChunkNativeJobRf3Receipt? FindMatch(IEnumerable<LogLine> actualBatch,
        Guid commandId, bool executing)
    {
        foreach (var line in actualBatch)
        {
            var receipt = SampleChunkNativeJobRf3Message.Read(line.Content, commandId, executing);
            if (receipt is not null) { return receipt; }
        }
        return null;
    }
}
