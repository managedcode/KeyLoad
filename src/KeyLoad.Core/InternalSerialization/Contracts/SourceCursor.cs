namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.SourceCursor)]
internal sealed record SourceCursor(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Source)] EventSourceRef Source,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.SchemaVersion)] long SchemaVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.SourceCursorFields.ExpiresAt)] DateTimeOffset ExpiresAt);
