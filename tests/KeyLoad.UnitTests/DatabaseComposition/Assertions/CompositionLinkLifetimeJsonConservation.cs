using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.Authorization;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonAssertions;
using P = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonProtocol;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionLinkLifetimeJsonConservation(string[] Rows, byte[] Identity, OutboxStatus Outbox,
    long Position, (long LastIndex, long CommittedIndex, long Term) Journal)
{
    private const int MaximumRows = 512;

    internal static CompositionLinkLifetimeJsonConservation Capture(SafeProjectorOperationDatabase owner, ReplicatedOperation operation)
        => new(RowsExceptFailure(owner, operation), NativeSerialization.Serialize(owner.Store.Identity),
            owner.Database.GetOutboxStatus(P.Root, owner.Source.Partition), owner.Store.Position, owner.JournalCut());

    internal async Task RequireFailureAsync(SafeProjectorOperationDatabase owner, ReplicatedOperation operation, string detail)
    {
        await Assert.That(RowsExceptFailure(owner, operation).SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).SequenceEqual(Identity)).IsTrue();
        await A.EqualAsync(Outbox, owner.Database.GetOutboxStatus(P.Root, owner.Source.Partition));
        await Assert.That(owner.Store.Position).IsEqualTo(Position + 1);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(Journal.LastIndex + 1);
        await Assert.That(owner.JournalCut()).IsEqualTo((Journal.LastIndex + 1, Journal.CommittedIndex + 1, Journal.Term));
        var expected = new StoredOutcome(NativeOperationFingerprint.Compute(operation), owner.Store.Identity.Incarnation,
            1, new(null, ErrorCode.Validation, detail))
        {
            ScopeKind = CommandOutcomeScopeKind.Partition,
            Partition = owner.Source.Partition
        };
        var outcome = OutcomeStoreOracle.ReadStored(owner.Store, operation);
        await Assert.That(NativeSerialization.Serialize(outcome).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        var partition = owner.Source.Partition;
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(CommandOutcomePartitionLocatorSerialization.ScopedKey(
            partition, P.Root, operation.Id)))!.SequenceEqual(OutcomeStoreOracle.PartitionKey(partition, P.Root, operation.Id))).IsTrue();
        await Assert.That(owner.Store.Read(view => NativeSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.Applied.ToArray())!))).IsEqualTo(Journal.LastIndex + 1);
        await Assert.That(owner.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.Clock.ToArray())!))).IsEqualTo(operation.EvaluatedAt);
        var retained = owner.Entry(operation).Operation!;
        await Assert.That(NativeSerialization.Serialize(retained).SequenceEqual(NativeSerialization.Serialize(operation))).IsTrue();
        await Assert.That(retained.PayloadJson).IsEqualTo(operation.PayloadJson);
        await Assert.That(retained.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        await Assert.That(NativeOperationFingerprint.Compute(retained)).IsEqualTo(NativeOperationFingerprint.Compute(operation));
    }

    private static string[] RowsExceptFailure(SafeProjectorOperationDatabase owner, ReplicatedOperation operation)
    {
        byte[][] excluded = [OutcomeStoreOracle.PartitionKey(owner.Source.Partition, P.Root, operation.Id),
            CommandOutcomePartitionLocatorSerialization.ScopedKey(owner.Source.Partition, P.Root, operation.Id),
            KeySpace.Clock.ToArray(), KeySpace.Applied.ToArray()];
        return owner.Store.Read(view =>
        {
            var page = view.Scan([], MaximumRows);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The genuine complete fixture exceeded its bounded image.");
            }

            return page.Records.Where(row => !excluded.Any(key => row.Key.Span.SequenceEqual(key)))
                .Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
        });
    }
}
