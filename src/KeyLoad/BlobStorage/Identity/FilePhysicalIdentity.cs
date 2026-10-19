using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Server;

internal static class FilePhysicalIdentity
{
    private const string PhysicalFamily = "file-content";
    private const int MaximumUploadIdCharacters = 128;
    private const int GuidBytes = 16;
    private const int HashStart = 0;
    private const char IdentitySeparator = '\0';
    internal static string Resource(FileAddress address, ResourceIncarnation incarnation)
        => Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode(PhysicalFamily,
            address.Partition.TenantId, address.Partition.DatabaseId, address.Partition.TransactionDomainId,
            address.Partition.PartitionKey, address.Resource, incarnation.Value)));

    internal static Guid Upload(string scope, string principal, string publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId) || publicId.Length > MaximumUploadIdCharacters || publicId.Any(char.IsControl))
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(IdentitySeparator, scope, principal, publicId)));
        return new Guid(hash.AsSpan(HashStart, GuidBytes));
    }

    internal static string Object(Guid objectId)
    {
        if (objectId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
        }
        return objectId.ToString(FileStorageProtocol.GuidFormat);
    }
}
