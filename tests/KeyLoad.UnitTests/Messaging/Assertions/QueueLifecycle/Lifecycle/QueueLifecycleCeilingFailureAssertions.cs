using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleCeilingFailureAssertions
{
    private const string Exhausted = "The queue lifecycle version or sequence is exhausted.";
    private const int FailureCommit = 1;
    private const string Separator = ":";

    internal static async Task<OperationResult> FailAsync(DatabaseEngine database, QueueLifecycleCeilingRows rows,
        string principal, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var store = (ZoneTreeStore)database.Store;
        var domain = Domain(store, rows, principal);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var result = Apply(database, rows, principal, time);
        await RefusalAsync(result);
        await Assert.That(store.Position).IsEqualTo(position + FailureCommit);
        await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That(Domain(store, rows, principal)).IsEquivalentTo(domain, CollectionOrdering.Matching);
        await Assert.That(QueueWholeFlowStorage.Clock(store)).IsEqualTo(time);
        await Assert.That(OutcomeStoreOracle.ReadPartition(store, rows.Lane.Partition, principal, rows.Request.CommandId))
            .IsEqualTo(result);
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(rows.Lane.Partition,
            principal, rows.Request.CommandId)));
        await Assert.That(locator).IsNotNull();
        await Assert.That(locator!.AsSpan().SequenceEqual(KeySpace.PartitionOutcome(rows.Lane.Partition,
            principal, rows.Request.CommandId))).IsTrue();
        await ReplayAsync(database, rows, result, principal, time, token);
        return result;
    }

    internal static async Task ReplayAsync(DatabaseEngine database, QueueLifecycleCeilingRows rows,
        OperationResult original, string principal, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var store = (ZoneTreeStore)database.Store;
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var actual = Apply(database, rows, principal, time);
        await RefusalAsync(actual);
        await Assert.That(actual).IsEqualTo(original);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
    }

    private static OperationResult Apply(DatabaseEngine database, QueueLifecycleCeilingRows rows,
        string principal, DateTimeOffset time)
        => rows.Delivery is { } delivery
            ? database.Apply(new(delivery.CommandId, OperationKind.Delivery, principal, time,
                JsonSerializer.Serialize(delivery, JsonDefaults.Options)))
            : database.Apply(new(rows.Request.CommandId, OperationKind.Batch, principal, time,
                JsonSerializer.Serialize(rows.Request, JsonDefaults.Options)));

    private static async Task RefusalAsync(OperationResult actual)
    {
        await Assert.That(actual.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(actual.SafeDetail).IsEqualTo(Exhausted);
        await Assert.That(actual.Json).IsNull();
        await Assert.That(actual.NativeValue).IsNull();
    }

    private static string[] Domain(ZoneTreeStore store, QueueLifecycleCeilingRows rows, string principal)
    {
        var outcome = Convert.ToHexString(KeySpace.PartitionOutcome(rows.Lane.Partition, principal, rows.Request.CommandId));
        var clock = Convert.ToHexString(KeySpace.ClockBytes);
        var locator = Convert.ToHexString(KeySpace.OutcomeLocatorV2(rows.Lane.Partition, principal, rows.Request.CommandId));
        return QueueWholeFlowStorage.Bytes(store).Where(row => !row.StartsWith(outcome + Separator, StringComparison.Ordinal)
            && !row.StartsWith(clock + Separator, StringComparison.Ordinal)
            && !row.StartsWith(locator + Separator, StringComparison.Ordinal)).ToArray();
    }
}
