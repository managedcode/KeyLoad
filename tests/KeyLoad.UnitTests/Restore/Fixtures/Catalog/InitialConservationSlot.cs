using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.BackupRestore.InitialConservationLineageProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

/// <summary>Test-owned explicit offline mapping, not a hosted CLUSTER topology or a terminal retirement issuer.</summary>
internal sealed class InitialConservationSlot : IDisposable
{
    private readonly byte[] signer = RandomNumberGenerator.GetBytes(P.SigningBytes);
    internal InitialConservationArchive Archive
    {
        get;
    }
    internal ClusterRestoreOwnerMapping Mapping
    {
        get;
    }
    internal ClusterRestoreSlotContext Context
    {
        get;
    }
    internal string Destination
    {
        get;
    }

    internal InitialConservationSlot(InitialConservationArchive archive, string destination)
    {
        Archive = archive;
        Destination = destination;
        var target = archive.Cut.Owner with
        {
            PhysicalShardId = Guid.NewGuid(),
            Incarnation = Guid.NewGuid()
        };
        Mapping = new(ClusterRestoreOwnerMapping.CurrentVersion, archive.Cut.Owner, target, [.. P.Endpoints]);
        Context = new(ClusterRestoreSlotContext.CurrentVersion, Guid.NewGuid(),
            ClusterRestoreMappingDigest.Compute([Mapping]), P.Empty, archive.Capture.ManifestDigest,
            archive.Identity.NodeId, archive.Identity.Incarnation, archive.Capture.Position, Guid.NewGuid(),
            target.Incarnation, Convert.ToHexStringLower(SHA256.HashData(signer)));
    }

    internal NativeCatalogRestoreSlotCompletion ExpectedCompletion => new(
        NativeCatalogRestoreSlotCompletion.CurrentVersion, Context, Context.TargetNodeId,
        Mapping.Target.Incarnation, Context.TargetSignerFingerprint,
        checked(Context.SourcePosition + P.One), checked(Context.SourcePosition + P.Two),
        checked(Context.SourcePosition + P.Two));

    internal NativeCatalogRestoreSlotCompletion Restore(CancellationToken token)
    {
        var limits = UnitExecutionOptions.DatabaseLimits();
        var work = new ReadExecutionBudget(limits, TimeProvider.System, token);
        return ZoneTreeStore.RestoreCatalogBackupSlot(Archive.Directory, Destination,
            Destination + P.StageSuffix, UnitExecutionOptions.StorageExecution(), TimeProvider.System, Context, signer,
            (transaction, identity, next, metadata, admitted) =>
                _ = ClusterRestoreCatalogReconciliation.ReconcileSlot(transaction, identity,
                    admitted.SourcePosition, next, metadata, P.Credential, [Archive.Cut], [Mapping], admitted,
                    limits, TimeProvider.System, work),
            (view, identity, position, admitted) => Verify(view, identity, position, admitted, work, token),
            null, token, (_, identity, position, admitted) =>
            {
                if (identity.NodeId != Archive.Identity.NodeId || identity.Incarnation != Archive.Identity.Incarnation
                    || position != Archive.Capture.Position || admitted != Context)
                {
                    throw new InvalidOperationException("The test resolver must retain its admitted original source.");
                }

                return Mapping;
            });
    }

    private void Verify(IKeyValueView target, StoreIdentity identity, long position,
        ClusterRestoreSlotContext admitted, ReadExecutionBudget work, CancellationToken token)
        => _ = ZoneTreeStore.ReadVerifiedCatalogBackup(Archive.Directory, UnitExecutionOptions.StorageExecution(),
            (source, original, sourcePosition, metadata) =>
            {
                if (sourcePosition != Archive.Capture.Position)
                {
                    throw new InvalidOperationException("The actual archive source position changed.");
                }

                ClusterRestoreImageComparison.Require(source, original, metadata, target, identity, position,
                    P.Credential, [Archive.Cut], [Mapping], admitted, UnitExecutionOptions.DatabaseLimits(),
                    TimeProvider.System, work);
                return true;
            }, token);

    public void Dispose() => CryptographicOperations.ZeroMemory(signer);
}
