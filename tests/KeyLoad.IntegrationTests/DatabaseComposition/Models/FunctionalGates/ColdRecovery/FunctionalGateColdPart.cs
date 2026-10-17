namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdPart(WriteBlobPartRequest Request, BlobCommitResult<BlobUploadInfo> Result);
