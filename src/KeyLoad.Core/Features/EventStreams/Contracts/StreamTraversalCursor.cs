namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(StreamTraversalCursor.SerializerAlias)]
internal sealed record StreamTraversalCursor(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] string Purpose,
    [property: Orleans.Id(2)] Guid Incarnation,
    [property: Orleans.Id(3)] StreamRef Stream,
    [property: Orleans.Id(4)] string PrincipalId,
    [property: Orleans.Id(5)] long PolicyEpoch,
    [property: Orleans.Id(6)] long SchemaVersion,
    [property: Orleans.Id(7)] long PlacementEpoch,
    [property: Orleans.Id(8)] StreamReadDirection Direction,
    [property: Orleans.Id(9)] long CapturedTailRevision,
    [property: Orleans.Id(10)] long CapturedFirstAvailableRevision,
    [property: Orleans.Id(11)] long SnapshotCutPosition,
    [property: Orleans.Id(12)] long NextExclusiveRevision,
    [property: Orleans.Id(13)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(14)] long LogicalPlacementRevision,
    [property: Orleans.Id(15)] long DirectoryFence)
{
    internal const string SerializerAlias = "keyload.core.stream-traversal-cursor.v1";
}
