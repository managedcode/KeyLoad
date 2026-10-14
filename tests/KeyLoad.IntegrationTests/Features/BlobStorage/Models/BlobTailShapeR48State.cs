namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobTailShapeR48State(BlobPartIdentityR40State original)
{
    private const string ShortSha = "8e1336ab78ebe687fd8056a37f2d3b0c32f4cf8fa8b691b653800fa693d570b9";
    private const string LongSha = "fbcb89ad6e6b6c69d2ec9623ed1fad1529bd39c1b766519d6dbaa3b9d1a3d527";
    internal BlobPartIdentityR40State Original { get; } = original;
    internal WriteBlobPartRequest[] Requests
    {
        get;
    } =
    [
        new(Guid.NewGuid(), original.Begin.Blob, original.Begin.UploadId, 1,
            new byte[] { 16, 32, 48 }, ShortSha),
        new(Guid.NewGuid(), original.Begin.Blob, original.Begin.UploadId, 1,
            new byte[] { 16, 32, 48, 64, 80 }, LongSha)
    ];
}
