using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorKeys
{
    private const string MapSpace = "event-vector-map-v1";
    private const string PageSpace = "event-vector-page-v1";
    private const string CapacitySpace = "event-vector-capacity-v1";
    private const string OfferSpace = "event-vector-offer-v1";
    private const string PhaseSpace = "event-vector-source-phase-v1";
    private const string SourceCapacitySpace = "event-vector-source-capacity-v1";
    private const string CoverageSpace = "event-vector-coverage-proof-v1";
    private const string CleanupCallSpace = "event-vector-cleanup-call-v1";
    private const string ParentCallSpace = "event-vector-parent-call-v1";
    private const string ParentOutcomeSpace = "event-vector-parent-outcome-index-v1";
    private const string PinSpace = "event-vector-source-pin-v1";
    internal static byte[] CleanupCalls(PartitionRef partition) => KeySpace.Partition(CleanupCallSpace, partition);
    internal static byte[] CleanupCall(PartitionRef partition, Guid mapId)
        => KeySpace.Partition(CleanupCallSpace, partition, mapId);

    internal static byte[] ParentCalls(PartitionRef partition) => KeySpace.Partition(ParentCallSpace, partition);
    internal static byte[] ParentOutcomes(PartitionRef partition) => KeySpace.Partition(ParentOutcomeSpace, partition);
    internal static byte[] ParentCall(PartitionRef partition, Guid mapId)
        => KeySpace.Partition(ParentCallSpace, partition, mapId);
    internal static byte[] ParentOutcome(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.Partition(ParentOutcomeSpace, partition, principalId, commandId);

    internal static byte[] Pins(PartitionRef partition) => KeySpace.Partition(PinSpace, partition);
    internal static byte[] SourceCapacity(PartitionRef partition) => KeySpace.Partition(SourceCapacitySpace, partition);

    internal static byte[] Maps(PartitionRef partition) => KeySpace.Partition(MapSpace, partition);
    internal static byte[] Pages(PartitionRef partition) => KeySpace.Partition(PageSpace, partition);
    internal static byte[] Proofs(PartitionRef partition) => KeySpace.Partition(CoverageSpace, partition);
    internal static byte[] Offers(PartitionRef partition) => KeySpace.Partition(OfferSpace, partition);
    internal static byte[] Phases(PartitionRef partition) => KeySpace.Partition(PhaseSpace, partition);

    internal static byte[] Pages(PartitionRef partition, Guid mapId) => KeySpace.Partition(PageSpace, partition, mapId);
    internal static byte[] Phases(PartitionRef partition, Guid mapId) => KeySpace.Partition(PhaseSpace, partition, mapId);

    internal static byte[] Map(PartitionRef partition, Guid mapId)
        => KeySpace.Partition(MapSpace, partition, mapId);
    internal static byte[] Page(PartitionRef partition, Guid mapId, int ordinal)
        => KeySpace.Partition(PageSpace, partition, mapId, ordinal);
    internal static byte[] Capacity(PartitionRef partition)
        => KeySpace.Partition(CapacitySpace, partition);
    internal static byte[] Offer(PartitionRef partition, Guid mapId)
        => KeySpace.Partition(OfferSpace, partition, mapId);
    internal static byte[] Phase(PartitionRef partition, Guid mapId, Guid commandId)
        => KeySpace.Partition(PhaseSpace, partition, mapId, commandId);
    internal static byte[] Proofs(PartitionRef partition, Guid mapId)
        => KeySpace.Partition(CoverageSpace, partition, mapId);
    internal static byte[] Proof(PartitionRef partition, Guid mapId, long coverageGeneration)
        => KeySpace.Partition(CoverageSpace, partition, mapId, coverageGeneration);

    internal static byte[] Pin(EventSourceRef source, Guid mapId)
        => KeySpace.Partition(PinSpace, source.Partition, mapId,
            Convert.ToHexString(SHA256.HashData(KeyCodec.Encode(source.Resource, (int)source.Kind, source.StreamId, source.Generation))));
}
