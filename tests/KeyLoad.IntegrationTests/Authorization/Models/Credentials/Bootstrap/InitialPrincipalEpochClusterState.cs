using KeyLoad.Core;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record InitialPrincipalEpochClusterState(PartitionRef Partition, string PrincipalId, string KeyId,
    CommandRequest Seed, CommitReceipt SeedReceipt, Guid RefusedId, PrincipalRecord Refused)
{
    internal Guid PrincipalCommandId { get; } = Guid.NewGuid();
    internal const string Collection = "epoch-documents";
    internal const string InvalidDetail = "An initial principal policy epoch must be positive.";
    internal const string Unavailable = "The credential is unavailable or expired.";
    internal string OriginalSecret => KeyId + ".original-credential-at-least-32";
    internal string ReplacementSecret => KeyId + ".replacement-credential-at-least-32";
    internal EntityRef Reference => new(Partition, Collection, "record");
    internal PrincipalRecord Principal(long epoch, bool granted = true, bool revoked = false)
        => new(PrincipalId, Partition.TenantId,
            granted ? [new(Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.DocumentsWrite)] : [], [])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
    internal ApiKeyRecord Credential(bool replacement = false, bool expired = false)
        => AuthorizationExecution.Credential(KeyId, PrincipalId, replacement ? ReplacementSecret : OriginalSecret,
            expired ? DateTimeOffset.UnixEpoch : null);
}
