using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Orleans;

internal static class DueWorkCommandIdentity
{
    private const string IdentityDomain = "keyload-due-command-v1";

    internal static Guid Create(DueWorkHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        ArgumentNullException.ThrowIfNull(hint.Lane);
        ArgumentNullException.ThrowIfNull(hint.Lane.Partition);
        var key = KeyCodec.Encode(IdentityDomain, (long)hint.Kind, hint.Lane.Partition.TenantId,
            hint.Lane.Partition.DatabaseId, hint.Lane.Partition.TransactionDomainId,
            hint.Lane.Partition.PartitionKey, hint.Lane.Queue, hint.Id.ToString(DueWorkFields.GuidFormat),
            hint.Revision, hint.Generation, hint.Ordinal);
        var hash = SHA256.HashData(key);
        var id = new Guid(hash.AsSpan(0, 16));
        return id == Guid.Empty ? new Guid(hash.AsSpan(16, 16)) : id;
    }
}
