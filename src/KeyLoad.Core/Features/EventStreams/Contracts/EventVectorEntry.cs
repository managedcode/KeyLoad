namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorEntry.SerializerAlias)]
internal sealed record EventVectorEntry
{
    internal const string SerializerAlias = "keyload.core.event-vector-entry.v1";

    [Orleans.Id(0)]
    public EventSourceRef Source { get; init; } = null!;

    [Orleans.Id(1)]
    public long Position { get; init; }

    [Orleans.Id(2)]
    public long EventSequence { get; init; }

    [Orleans.Id(3)]
    public long CapturedCut { get; init; }

    [Orleans.Id(4)]
    public Guid Incarnation { get; init; }

    [Orleans.Id(5)]
    public long PlacementEpoch { get; init; }

    [Orleans.Id(6)]
    public string PrincipalId { get; init; } = null!;

    [Orleans.Id(7)]
    public long PolicyEpoch { get; init; }

    [Orleans.Id(8)]
    public long SchemaVersion { get; init; }

    [Orleans.Id(9)]
    public long LogicalPlacementRevision { get; init; }

    [Orleans.Id(10)]
    public long DirectoryFence { get; init; }

    [Orleans.Id(11)]
    public long FirstAvailablePosition { get; init; }

    [Orleans.Id(12)]
    public long CapturedTailPosition { get; init; }
}
