namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobPartMetadata)]
internal sealed record BlobPartMetadata(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobPartMetadataFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobPartMetadataFields.Length)] int Length,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobPartMetadataFields.Sha256)] string Sha256);
