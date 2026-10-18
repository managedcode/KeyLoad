using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropRuntimeAssertions
{
    internal sealed record Image(Guid NodeId, Guid Incarnation, long Position, long Applied, string[] Rows);

    internal static T Value<T>(GrainOperationReply reply) where T : class
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? NativeDropRuntimeProtocol.MissingReply);
        }
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as T
            ?? throw Errors.Fail(ErrorCode.Corruption, NativeDropRuntimeProtocol.MissingReply);
    }

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task<Image[]> CaptureAsync(NativeDropRuntimeProfile profile, CancellationToken token)
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

    internal static async Task SameImageAsync(NativeDropRuntimeProfile profile, Image[] before, CancellationToken token)
    {
        var after = await CaptureAsync(profile, token);
        await Assert.That(after.Length).IsEqualTo(NativeDropRuntimeProtocol.NativeNodes);
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = NativeDropRuntimeProtocol.FirstCold; index < before.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await Assert.That(after[index].Position).IsEqualTo(before[index].Position);
            await Assert.That(after[index].Applied).IsEqualTo(before[index].Applied);
            await SameAsync(before[index].Rows, after[index].Rows);
        }
    }

    internal static async Task FailureImageAsync(NativeDropRuntimeProfile profile, Guid commandId, Image[] before,
        ErrorCode error, CancellationToken token)
    {
        var after = await CaptureAsync(profile, token);
        var key = OutcomeStoreOracle.GlobalKey(PartitionStoreProtocol.AdministratorId, commandId);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(key), Convert.ToHexString(KeySpace.ClockBytes), Convert.ToHexString(KeySpace.AppliedBytes)
        };
        await Assert.That(after.Length).IsEqualTo(NativeDropRuntimeProtocol.NativeNodes);
        for (var index = NativeDropRuntimeProtocol.FirstCold; index < after.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await SameAsync(Filter(before[index].Rows, allowed), Filter(after[index].Rows, allowed));
            var outcome = profile.Hosts.Nodes[index].Database.Store.Read(view => view.GetRecord<StoredOutcome>(key));
            await Assert.That(outcome).IsNotNull();
            await Assert.That(Errors.Code(outcome!.Result.Problem)).IsEqualTo((ErrorCode?)error);
            await Assert.That(outcome.Result.Value).IsNull();
            await Assert.That(outcome.PreparedCommand.CommandId).IsEqualTo(commandId);
            await Assert.That(outcome.CanonicalSequence).IsEqualTo(before[index].Applied + NativeDropRuntimeProtocol.NextCommit);
            await Assert.That(after[index].Applied).IsEqualTo(outcome.CanonicalSequence);
            await Assert.That(after[index].Position).IsEqualTo(before[index].Position + NativeDropRuntimeProtocol.NextCommit);
        }
    }

    internal static async Task<byte[]> StoredDropAsync(NativeDropRuntimeProfile profile, Guid commandId,
        DropTableRequest request, ErrorCode? error, CancellationToken token)
    {
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Database.Coordinator.ReadBarrierAsync(token)));
        var key = OutcomeStoreOracle.GlobalKey(PartitionStoreProtocol.AdministratorId, commandId);
        byte[]? original = null;
        foreach (var node in profile.Hosts.Nodes)
        {
            var bytes = node.Database.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingReply);
            var outcome = NativeSerialization.Deserialize<StoredOutcome>(bytes);
            await Assert.That(outcome.CanonicalSequence).IsGreaterThan(default(long));
            await Assert.That(outcome.PreparedCommand.CommandId).IsEqualTo(commandId);
            await Assert.That(outcome.PreparedCommand.UserId).IsEqualTo(PartitionStoreProtocol.AdministratorId);
            await Assert.That(outcome.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.DropTable);
            await Assert.That(outcome.PreparedCommand.Value.Input.Span.SequenceEqual(NativeSerialization.Serialize(request))).IsTrue();
            await Assert.That(Errors.Code(outcome.Result.Problem)).IsEqualTo(error);
            if (original is not null)
            {
                await Assert.That(bytes.SequenceEqual(original)).IsTrue();
            }
            original = bytes;
        }
        return original ?? throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingReply);
    }

    internal static async Task DroppedAsync(NativeDropRuntimeProfile profile, Guid commandId,
        DropTableRequest request, DropTableResult actual, CancellationToken token)
    {
        var stored = NativeSerialization.Deserialize<StoredOutcome>(await StoredDropAsync(profile, commandId, request, null, token));
        var expected = new DropTableResult(request.TenantId, request.DatabaseId, request.ResourceName,
            new(commandId, new(profile.Source.Store.Identity.Incarnation, NativeCatalogRoute.PartitionId,
                stored.CanonicalSequence), [], DurabilityProfile.ProcessDurable, NativeDropRuntimeProtocol.PersistedCopies));
        await SameAsync(expected, actual);
        await Assert.That(stored.Result.Get<DropTableResult>().Receipt.PersistedCopies)
            .IsEqualTo(NativeDropRuntimeProtocol.LocalPersistedCopies);
        foreach (var node in profile.Hosts.Nodes)
        {
            var absent = node.Database.Store.Read(view =>
                view.ReadOwnedValue(KeySpace.Resource(request.TenantId, request.DatabaseId, request.ResourceName)) is null
                && view.ReadOwnedValue(ResourceLifetimeCatalog.Key(request.TenantId, request.DatabaseId, request.ResourceName)) is null
                && view.ReadOwnedValue(DocumentStorageKeys.RecordKey(new(NativeDropRuntimeProtocol.Partition,
                    request.ResourceName, NativeDropRuntimeProtocol.Row))) is null);
            await Assert.That(absent).IsTrue();
        }
    }

    private static string[] Filter(string[] rows, HashSet<string> excluded)
        => rows.Where(row => !excluded.Contains(row[..row.IndexOf(':', StringComparison.Ordinal)])).ToArray();
}
