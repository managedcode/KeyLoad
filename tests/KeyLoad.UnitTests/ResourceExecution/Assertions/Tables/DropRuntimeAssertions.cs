using KeyLoad.Clustering;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Messaging;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class DropRuntimeAssertions
{
    internal sealed record Image(Guid NodeId, Guid Incarnation, long Position, long Applied, string[] Rows);

    internal static T Value<T>(GrainOperationReply reply) where T : class
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? DropRuntimeProtocol.MissingReply);
        }
        return GrainPayload.Read<GrainValue>(reply.Payload).Value as T
            ?? throw Errors.Fail(ErrorCode.Corruption, DropRuntimeProtocol.MissingReply);
    }

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task<Image[]> CaptureAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Database.Coordinator.ReadBarrierAsync(token)));
        return profile.Hosts.Nodes.Select(node =>
        {
            var owner = node.Database;
            var cut = owner.ReadCanonicalCut();
            return new Image(owner.Store.Identity.NodeId, owner.Store.Identity.Incarnation,
                cut.StorePosition, cut.AppliedPosition, QueueWholeFlowStorage.Bytes(owner.Store));
        }).ToArray();
    }

    internal static async Task SameImageAsync(DropRuntimeProfile profile, Image[] before, CancellationToken token)
    {
        var after = await CaptureAsync(profile, token);
        await Assert.That(after.Length).IsEqualTo(DropRuntimeProtocol.RuntimeNodes);
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = DropRuntimeProtocol.FirstCold; index < before.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await Assert.That(after[index].Position).IsEqualTo(before[index].Position);
            await Assert.That(after[index].Applied).IsEqualTo(before[index].Applied);
            await SameAsync(before[index].Rows, after[index].Rows);
        }
    }

    internal static async Task FailureImageAsync(DropRuntimeProfile profile, Guid commandId, Image[] before,
        ErrorCode error, CancellationToken token)
    {
        var after = await CaptureAsync(profile, token);
        var key = OutcomeStoreOracle.GlobalKey(PartitionStoreProtocol.AdministratorId, commandId);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(key), Convert.ToHexString(KeySpace.ClockBytes), Convert.ToHexString(KeySpace.AppliedBytes)
        };
        await Assert.That(after.Length).IsEqualTo(DropRuntimeProtocol.RuntimeNodes);
        for (var index = DropRuntimeProtocol.FirstCold; index < after.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await SameAsync(Filter(before[index].Rows, allowed), Filter(after[index].Rows, allowed));
            var outcome = profile.Hosts.Nodes[index].Database.Store.Read(view => view.GetRecord<StoredOutcome>(key));
            await Assert.That(outcome).IsNotNull();
            await Assert.That(Errors.Code(outcome!.Result.Problem)).IsEqualTo((ErrorCode?)error);
            await Assert.That(outcome.Result.Value).IsNull();
            await Assert.That(outcome.PreparedCommand.CommandId).IsEqualTo(commandId);
            await Assert.That(outcome.CanonicalSequence).IsEqualTo(before[index].Applied + DropRuntimeProtocol.NextCommit);
            await Assert.That(after[index].Applied).IsEqualTo(outcome.CanonicalSequence);
            await Assert.That(after[index].Position).IsEqualTo(before[index].Position + DropRuntimeProtocol.NextCommit);
        }
    }

    internal static async Task<byte[]> StoredDropAsync(DropRuntimeProfile profile, Guid commandId,
        DropTableRequest request, ErrorCode? error, CancellationToken token)
    {
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Database.Coordinator.ReadBarrierAsync(token)));
        var key = OutcomeStoreOracle.GlobalKey(PartitionStoreProtocol.AdministratorId, commandId);
        byte[]? original = null;
        foreach (var node in profile.Hosts.Nodes)
        {
            var bytes = node.Database.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException(DropRuntimeProtocol.MissingReply);
            var outcome = OrleansSerialization.Deserialize<StoredOutcome>(bytes);
            await Assert.That(outcome.CanonicalSequence).IsGreaterThan(default(long));
            await Assert.That(outcome.PreparedCommand.CommandId).IsEqualTo(commandId);
            await Assert.That(outcome.PreparedCommand.UserId).IsEqualTo(PartitionStoreProtocol.AdministratorId);
            await Assert.That(outcome.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.DropTable);
            await Assert.That(outcome.PreparedCommand.Value.Input.Span.SequenceEqual(OrleansSerialization.Serialize(request))).IsTrue();
            await Assert.That(Errors.Code(outcome.Result.Problem)).IsEqualTo(error);
            if (original is not null)
            {
                await Assert.That(bytes.SequenceEqual(original)).IsTrue();
            }
            original = bytes;
        }
        return original ?? throw new InvalidOperationException(DropRuntimeProtocol.MissingReply);
    }

    internal static async Task DroppedAsync(DropRuntimeProfile profile, Guid commandId,
        DropTableRequest request, DropTableResult actual, CancellationToken token)
    {
        var stored = OrleansSerialization.Deserialize<StoredOutcome>(await StoredDropAsync(profile, commandId, request, null, token));
        var expected = new DropTableResult(request.TenantId, request.DatabaseId, request.ResourceName,
            new(commandId, new(profile.Source.Store.Identity.Incarnation, CatalogRoute.PartitionId,
                stored.CanonicalSequence), [], DurabilityProfile.ProcessDurable, DropRuntimeProtocol.PersistedCopies));
        await SameAsync(expected, actual);
        await Assert.That(stored.Result.Get<DropTableResult>().Receipt.PersistedCopies)
            .IsEqualTo(DropRuntimeProtocol.LocalPersistedCopies);
        foreach (var node in profile.Hosts.Nodes)
        {
            var absent = node.Database.Store.Read(view =>
                view.ReadOwnedValue(KeySpace.Resource(request.TenantId, request.DatabaseId, request.ResourceName)) is null
                && view.ReadOwnedValue(ResourceLifetimeCatalog.Key(request.TenantId, request.DatabaseId, request.ResourceName)) is null
                && view.ReadOwnedValue(DocumentStorageKeys.RecordKey(new(DropRuntimeProtocol.Partition,
                    request.ResourceName, DropRuntimeProtocol.Row))) is null);
            await Assert.That(absent).IsTrue();
        }
    }

    private static string[] Filter(string[] rows, HashSet<string> excluded)
        => rows.Where(row => !excluded.Contains(row[..row.IndexOf(':', StringComparison.Ordinal)])).ToArray();
}
