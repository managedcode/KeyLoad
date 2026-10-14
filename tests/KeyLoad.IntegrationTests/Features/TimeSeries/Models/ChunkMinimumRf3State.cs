using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record ChunkMinimumRf3Window(Guid Id, string Series, long Revision, long Sequence,
    ImmutableArray<SampleRecord> Rows, CommitToken Minimum)
{
    internal ReadSampleChunkWindowRequest Request(PartitionRef partition)
        => new(partition, TimeSeriesRf3Scenario.Set, Series, Id, null, null, Rows.Length);
}

internal sealed record ChunkMinimumRf3Original(CommandRequest Request, CommitReceipt Receipt, string Node);

internal sealed record ChunkMinimumRf3State(PartitionRef Partition, string Administrator,
    McpPersistedIdentity Reader, PhysicalOwnerDirectoryV1 Directory,
    AtomicPartitionPlacementResolution OriginalOwner, List<ChunkMinimumRf3Window> Windows,
    List<ChunkMinimumRf3Original> Originals, OutboxStatus OriginalOutbox)
{
    internal AtomicPartitionPlacementResolution Owner { get; set; } = OriginalOwner;
    internal OutboxStatus Outbox { get; set; } = OriginalOutbox;
    internal PrincipalRecord CurrentReader { get; set; } = Reader.Principal;
    internal PartitionMoveRequest Move { get; } = new(Guid.NewGuid(), Partition,
        Directory.Owners.Single(entry => entry.Owner.PhysicalShardId != Directory.ControlOwner.PhysicalShardId)
            .Owner.PhysicalShardId, OriginalOwner.Revision, PartitionMoveMode.Transfer);
    internal string[] Nodes => Owner.PhysicalShardId == Directory.ControlOwner.PhysicalShardId
        ? TwoRf3MembershipProtocol.Nodes[..TwoRf3MembershipProtocol.MembersPerGroup]
        : TwoRf3MembershipProtocol.Nodes[TwoRf3MembershipProtocol.MembersPerGroup..];
}
