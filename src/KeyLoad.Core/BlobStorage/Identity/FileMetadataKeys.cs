using KeyLoad.Storage;

namespace KeyLoad.Core.BlobStorage;

internal static class FileMetadataKeys
{
    internal static byte[] Quota(FileAddress address, ResourceIncarnation incarnation)
        => Prefix(address, incarnation, FileStorageProtocol.QuotaFamily);

    internal static byte[] Head(FileAddress address, ResourceIncarnation incarnation)
        => Key(address, incarnation, FileStorageProtocol.HeadFamily, address.Path);

    internal static byte[] Upload(FileAddress address, ResourceIncarnation incarnation, Guid uploadId)
        => Key(address, incarnation, FileStorageProtocol.UploadFamily, uploadId.ToString(FileStorageProtocol.GuidFormat));

    internal static byte[] Prefix(FileAddress address, ResourceIncarnation incarnation, string family)
        => KeyCodec.Encode(family, address.Partition.TenantId, address.Partition.DatabaseId,
            address.Partition.TransactionDomainId, address.Partition.PartitionKey, address.Resource, incarnation.Value);

    private static byte[] Key(FileAddress address, ResourceIncarnation incarnation, string family, string identity)
        => KeyCodec.Encode(family, address.Partition.TenantId, address.Partition.DatabaseId,
            address.Partition.TransactionDomainId, address.Partition.PartitionKey, address.Resource, incarnation.Value, identity);

    internal static void Validate(FileAddress address, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(address);
        EngineResourceAccess.ValidatePartition(address.Partition);
        JsonData.Identifier(address.Resource);
        var path = address.Path;
        if (path is null || path.Length > FileStorageProtocol.MaximumPathCharacters
            || (!allowEmpty && path.Length == FileStorageProtocol.EmptyCount) || path.Contains(FileStorageProtocol.ForbiddenSeparator, StringComparison.Ordinal)
            || path.Any(char.IsControl) || (path.Length != FileStorageProtocol.EmptyCount && path.Split(FileStorageProtocol.PathSeparator).Any(p => p is FileStorageProtocol.EmptyPath or FileStorageProtocol.CurrentSegment or FileStorageProtocol.ParentSegment)))
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
    }
}
