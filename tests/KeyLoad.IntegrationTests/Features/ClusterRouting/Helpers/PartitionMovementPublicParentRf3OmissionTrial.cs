using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Genuine cold missing authority denies all public callers before exact raw restoration and healthy replay.</summary>
internal static class PartitionMovementPublicParentRf3OmissionTrial
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest request, PartitionMoveResult terminal,
        CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request, cancellationToken).ConfigureAwait(false);
        var originals = ReadHeaders(wave, request);
        await Assert.That(originals.Count).IsEqualTo(3);
        var failures = new List<Exception>();
        var stopped = true;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            WriteHeaders(wave, request, originals, remove: true);
            var faulted = ReadCuts(wave, request);
            stopped = false;
            await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
            await RequireDeniedAsync(seed, request with { Mode = PartitionMoveMode.Resume }, cancellationToken).ConfigureAwait(false);
            await RequireDeniedAsync(seed, request with { Mode = PartitionMoveMode.Abort }, cancellationToken).ConfigureAwait(false);
            var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request,
                cancellationToken).ConfigureAwait(false);
            stopped = true;
            await SqlRf3Protocol.EqualAsync(faulted, after);
        }, failures).ConfigureAwait(false);
        if (!stopped)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request, cancellationToken).ConfigureAwait(false);
                stopped = true;
            }, failures).ConfigureAwait(false);
        }
        if (stopped)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                WriteHeaders(wave, request, originals, remove: false);
                await RequireRestoredAsync(originals, ReadHeaders(wave, request));
            }, failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => PartitionMovementPublicParentRf3Cut.RestartAsync(
                wave, cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, request, terminal,
            cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireDeniedAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveRequest request, CancellationToken cancellationToken)
    {
        var denied = await seed.Source.MovePartitionAsync(request, cancellationToken).ConfigureAwait(false);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName,
            request, cancellationToken).ConfigureAwait(false), ErrorCode.RecoveryRequired, dispatched: true);
        var sql = SqlRf3Protocol.Call(request.Partition, PartitionMovePublicProtocol.ToolName, request);
        var deniedSql = await seed.Source.ExecuteSqlAsync(sql, cancellationToken).ConfigureAwait(false);
        await Assert.That(deniedSql.IsSuccess).IsFalse();
        await Assert.That(deniedSql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(SqlOperationProtocol.ToolName,
            sql, cancellationToken).ConfigureAwait(false), ErrorCode.RecoveryRequired, dispatched: true);
    }

    private static async Task RequireRestoredAsync(Dictionary<string, byte[]> original,
        Dictionary<string, byte[]> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(original.Count);
        foreach (var pair in original)
        {
            await Assert.That(actual.TryGetValue(pair.Key, out var restored)).IsTrue();
            await Assert.That(restored!.AsSpan().SequenceEqual(pair.Value)).IsTrue();
        }
    }

    private static Dictionary<string, byte[]> ReadHeaders(TwoRf3MembershipWave wave, PartitionMoveRequest request)
    {
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var original = WithStore(wave, node, store => store.Read(view =>
                view.ReadOwnedValue(PartitionMoveParentKeys.Header(request.Partition, request.MoveId))));
            if (original is not null)
            { originals.Add(node, original); }
        }
        return originals;
    }

    private static void WriteHeaders(TwoRf3MembershipWave wave, PartitionMoveRequest request,
        Dictionary<string, byte[]> originals, bool remove)
    {
        foreach (var pair in originals)
        {
            WithStore(wave, pair.Key, store => store.Commit((transaction, _) =>
            {
                var key = PartitionMoveParentKeys.Header(request.Partition, request.MoveId);
                if (remove)
                { transaction.Delete(key); }
                else
                { transaction.Put(key, pair.Value); }
                return true;
            }));
        }
    }

    private static T WithStore<T>(TwoRf3MembershipWave wave, string node, Func<ZoneTreeStore, T> read)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        T result = default!;
        ServerFailureObserver.Observe(() =>
        {
            store = Open(wave, node);
            result = read(store);
        }, failures);
        if (store is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static ZoneTreeStore Open(TwoRf3MembershipWave wave, string node)
        => new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
            IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());

    private static PartitionMovementPublicParentRf3NativeCut[] ReadCuts(TwoRf3MembershipWave wave, PartitionMoveRequest request)
        => TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
            wave, node, request, Guid.Empty)).ToArray();
}
