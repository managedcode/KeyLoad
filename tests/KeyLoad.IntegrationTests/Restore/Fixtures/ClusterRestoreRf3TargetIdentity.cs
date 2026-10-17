using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed class ClusterRestoreRf3TargetIdentity
{
    private const string ProfileRootName = "target-profile";
    private const string DataRootName = "target-data";
    private const int SecretBytes = 32;
    internal string ProfileRoot
    {
        get;
    }
    internal string DataRoot
    {
        get;
    }
    internal ImmutableArray<ClusterRestoreOwnerMapping> Mappings
    {
        get;
    }
    internal NodeEpochRf3Profile Profile
    {
        get;
    }
    internal string Peer
    {
        get;
    }

    internal ClusterRestoreRf3TargetIdentity(string ownedRoot, string credential,
        ImmutableArray<ClusterBackupOwnerReceipt> receipts)
    {
        ProfileRoot = Path.Combine(ownedRoot, ProfileRootName);
        DataRoot = Path.Combine(ownedRoot, DataRootName);
        Mappings = [.. receipts.Select(receipt => new ClusterRestoreOwnerMapping(
            ClusterRestoreRf3Protocol.CurrentVersion, receipt.Cut.Owner,
            new(Guid.NewGuid(), Guid.NewGuid(), receipt.Cut.Owner.VoterIds,
                ClusterRestoreRf3Protocol.InitialOwnerEpoch),
            [.. receipt.Cut.Owner.VoterIds.Select(voter => voter + "/")]))];
        var first = Mappings.First().Target;
        Profile = new(new LocalProfile(ClusterProfileStore.CurrentVersion, first.PhysicalShardId, first.Incarnation,
            Secret(), Secret(), credential));
        Peer = Secret();
    }

    internal static string Secret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        try
        {
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
