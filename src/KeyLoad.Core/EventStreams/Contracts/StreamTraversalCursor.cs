namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(StreamTraversalCursor.SerializerAlias)]
internal sealed record StreamTraversalCursor(
    [property: Orleans.Id(StreamTraversalCursorFields.Version)] int Version,
    [property: Orleans.Id(StreamTraversalCursorFields.Purpose)] string Purpose,
    [property: Orleans.Id(StreamTraversalCursorFields.Incarnation)] Guid Incarnation,
    [property: Orleans.Id(StreamTraversalCursorFields.Stream)] StreamRef Stream,
    [property: Orleans.Id(StreamTraversalCursorFields.Principal)] string PrincipalId,
    [property: Orleans.Id(StreamTraversalCursorFields.PolicyEpoch)] long PolicyEpoch,
    [property: Orleans.Id(StreamTraversalCursorFields.SchemaVersion)] long SchemaVersion,
    [property: Orleans.Id(StreamTraversalCursorFields.Direction)] StreamReadDirection Direction,
    [property: Orleans.Id(StreamTraversalCursorFields.Tail)] long CapturedTailRevision,
    [property: Orleans.Id(StreamTraversalCursorFields.FirstAvailable)] long CapturedFirstAvailableRevision,
    [property: Orleans.Id(StreamTraversalCursorFields.SnapshotCut)] long SnapshotCutPosition,
    [property: Orleans.Id(StreamTraversalCursorFields.NextExclusive)] long NextExclusiveRevision,
    [property: Orleans.Id(StreamTraversalCursorFields.ExpiresAt)] DateTimeOffset ExpiresAt)
{
    internal const string SerializerAlias = "keyload.core.stream-traversal-cursor";
}
