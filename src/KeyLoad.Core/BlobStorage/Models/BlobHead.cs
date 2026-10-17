namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobHead)]
internal sealed record BlobHead(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobHeadFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobHeadFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobHeadFields.Metadata)] BlobMetadata Metadata);
