using System.Collections.Immutable;

namespace KeyLoad.Core.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(FileStorageProtocol.EntryAlias)]
internal sealed record FileEntry([property: Orleans.Id(0)] FileAddress Address,
    [property: Orleans.Id(1)] ResourceIncarnation Incarnation, [property: Orleans.Id(2)] Guid ObjectId,
    [property: Orleans.Id(3)] long Revision, [property: Orleans.Id(4)] long Length,
    [property: Orleans.Id(5)] string ContentType, [property: Orleans.Id(6)] string Sha256,
    [property: Orleans.Id(7)] RowAccess Access, [property: Orleans.Id(8)] bool Deleted)
{
    [Orleans.Id(10)] public DateTimeOffset CreatedOn { get; init; }
    [Orleans.Id(11)] public DateTimeOffset ModifiedOn { get; init; }
    [Orleans.Id(9)]
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;
}
