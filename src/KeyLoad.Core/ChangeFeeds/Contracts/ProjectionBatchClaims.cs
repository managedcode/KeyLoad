namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreAliases.ProjectionBatchClaims)]
internal sealed record ProjectionBatchClaims(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Consumer)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.IndexGeneration)] long IndexGeneration,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.After)] long After,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.Through)] long Through,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionBatchClaimsFields.ExpiresAt)] DateTimeOffset ExpiresAt);
