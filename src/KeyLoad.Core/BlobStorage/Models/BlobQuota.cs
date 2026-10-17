namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobQuota)]
internal sealed record BlobQuota(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.ReservedBytes)] long ReservedBytes,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.ObjectKeys)] int ObjectKeys,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.Versions)] int Versions,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.Uploads)] int Uploads)
{
    private const int EmptyReservedBytes = 0;
    private const int EmptyObjectKeys = 0;
    private const int EmptyVersions = 0;
    private const int EmptyUploads = 0;

    internal static BlobQuota Empty(Guid incarnation) => new(BlobKeys.FormatVersion, incarnation, EmptyReservedBytes, EmptyObjectKeys, EmptyVersions, EmptyUploads);
}
