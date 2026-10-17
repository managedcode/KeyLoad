namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record NativeBlobWireSeedCommands(BeginBlobUploadRequest Begin,
    WriteBlobPartRequest Write, CompleteBlobUploadRequest Complete);
