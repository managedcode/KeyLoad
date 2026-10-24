namespace KeyLoad.Core.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(FileStorageProtocol.UploadAlias)]
internal sealed record FileUpload([property: Orleans.Id(0)] FileAddress Address,
    [property: Orleans.Id(1)] ResourceIncarnation Incarnation, [property: Orleans.Id(2)] long SchemaVersion,
    [property: Orleans.Id(3)] Guid UploadId, [property: Orleans.Id(4)] string Creator,
    [property: Orleans.Id(5)] long ExpectedRevision, [property: Orleans.Id(6)] long Length,
    [property: Orleans.Id(7)] string ContentType, [property: Orleans.Id(8)] RowAccess Access,
    [property: Orleans.Id(9)] Guid ObjectId, [property: Orleans.Id(10)] bool Published,
    [property: Orleans.Id(11)] bool Aborted, [property: Orleans.Id(12)] string TransferOwner);
