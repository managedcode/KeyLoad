namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.ProjectionBatchClaims)]
internal sealed record ProjectionBatchClaims(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.Consumer)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.IndexGeneration)] long IndexGeneration,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.After)] long After,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.Through)] long Through,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionBatchClaimsFields.ExpiresAt)] DateTimeOffset ExpiresAt);
