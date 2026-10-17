namespace KeyLoad.Core.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(ControlledBlobReadProtocol.PurposeAlias)]
internal enum ControlledBlobReadPurpose
{
    Outcome,
    Metadata,
    UploadInfo,
    Range,
    List
}
