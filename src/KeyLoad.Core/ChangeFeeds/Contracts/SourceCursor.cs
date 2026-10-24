namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.SourceCursor)]
internal sealed record SourceCursor(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.Source)] EventSourceRef Source,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.SchemaVersion)] long SchemaVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SourceCursorFields.ExpiresAt)] DateTimeOffset ExpiresAt);
