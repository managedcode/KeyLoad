using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reads actual protected A lineage only after genuine native finalize and checks its complete value.</summary>
internal static class ControlledPartitionMovementPublicationRead
{
    private const int Version = 1;
    private const long FirstDirectoryRevision = 1;
    private const long PublishedOwnershipEpoch = 2;

    internal static async Task<PartitionMovePublishedPlacement> ReadAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult actualFinalized)
    {
        var control = actualFinalized.Control
            ?? throw new InvalidOperationException("The actual Published control is absent.");
        var installed = control.InstalledReceipt
            ?? throw new InvalidOperationException("The actual Installed token is absent.");
        var actual = source.Store.Read(view => PartitionMovePublishedPlacementStorage.Read(view, control.Partition))
            ?? throw new InvalidOperationException("The actual protected A publication is absent.");
        var row = new AtomicPartitionPlacementV1(Version, control.Partition,
            corpus.Destination.Owner.PhysicalShardId, FirstDirectoryRevision,
            corpus.Destination.Owner.Incarnation, corpus.Destination.Owner.VoterIds, PublishedOwnershipEpoch);
        var expected = new PartitionMovePublishedPlacement(Version,
            ControlledPartitionMovementPrepareRequest.MoveId, row, control.SourcePlacement,
            corpus.Destination.Owner, installed, actualFinalized.Journal);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        return actual;
    }
}
