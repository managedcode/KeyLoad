using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class D2ArchivePlan : IDisposable
{
    private readonly byte[][] signing;
    private readonly string root;
    private readonly string digest;
    private readonly Guid operationId = Guid.NewGuid();
    internal ImmutableArray<ClusterBackupOwnerCut> Cuts
    {
        get;
    }
    internal ImmutableArray<ClusterRestoreOwnerMapping> Mappings
    {
        get;
    }

    internal D2ArchivePlan(D2ArchiveCapture[] archives, string root)
    {
        this.root = root;
        Cuts = archives.Select(archive => archive.Cut).ToImmutableArray();
        ClusterBackupOwnerClosureValidation.Require(Cuts.First().CaptureId, Cuts);
        Mappings = archives.Select((archive, index) =>
        {
            var voters = D2ArchiveData.TargetVoters(index).ToImmutableArray();
            var owner = new PhysicalShardRecord(Guid.NewGuid(), Guid.NewGuid(), voters, D2ArchiveData.Single);
            return new ClusterRestoreOwnerMapping(ClusterRestoreOwnerMapping.CurrentVersion, archive.Cut.Owner,
                owner, voters.Select(voter => new Uri(voter).AbsoluteUri).ToImmutableArray());
        }).ToImmutableArray();
        ClusterRestoreMappingValidation.Require(Cuts.First().CaptureId, Cuts, Mappings);
        digest = ClusterRestoreMappingDigest.Compute(Mappings);
        signing = archives.Select(_ => RandomNumberGenerator.GetBytes(D2ArchiveData.SigningKeyBytes)).ToArray();
    }

    internal D2ArchiveSlot Slot(D2ArchiveCapture archive, int ordinal, string name, Guid? distinctOperation = null)
    {
        var index = archive.Source.Ordinal;
        var mapping = Mappings[index];
        var context = new ClusterRestoreSlotContext(ClusterRestoreSlotContext.CurrentVersion,
            distinctOperation ?? operationId, digest, ordinal, archive.Native.ManifestDigest,
            archive.Cut.SourceNodeId, archive.Cut.Owner.Incarnation, archive.Cut.StorePosition,
            Guid.NewGuid(), mapping.Target.Incarnation, Convert.ToHexStringLower(SHA256.HashData(signing[index])));
        ClusterRestoreSlotContextValidation.Require(context);
        return new(archive, Cuts, Mappings, mapping, context, signing[index], Path.Combine(root, name));
    }

    public void Dispose()
    {
        foreach (var key in signing)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
