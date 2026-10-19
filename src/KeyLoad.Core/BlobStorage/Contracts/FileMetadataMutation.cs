using System.Collections.Immutable;

namespace KeyLoad.Core.Features.BlobStorage;

internal enum FileMetadataAction
{
    Reserve, Stage, Publish, Abort, Delete, Reclaim
}

[Orleans.GenerateSerializer, Orleans.Alias(FileStorageProtocol.MutationAlias)]
internal sealed record FileMetadataMutation([property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] FileAddress Address, [property: Orleans.Id(2)] FileMetadataAction Action,
    [property: Orleans.Id(3)] Guid UploadId, [property: Orleans.Id(4)] Guid ObjectId,
    [property: Orleans.Id(5)] long Length, [property: Orleans.Id(6)] string ContentType,
    [property: Orleans.Id(7)] string Sha256, [property: Orleans.Id(8)] RowAccess Access,
    [property: Orleans.Id(9)] string TransferOwner)
{
    [Orleans.Id(10)]
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;
}
