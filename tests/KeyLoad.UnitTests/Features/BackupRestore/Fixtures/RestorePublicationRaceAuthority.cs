using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class RestorePublicationRaceAuthority : IDisposable
{
    private const long NoAppliedIndex = 0;
    private const int RestoreCommits = 2;
    private static readonly ImmutableArray<string> Endpoints =
        ["http://127.0.0.1:18001/", "http://127.0.0.1:18002/", "http://127.0.0.1:18003/"];
    private readonly ClusterBackupOwnerCut original;
    private readonly ClusterRestoreOwnerMapping mapping;
    private readonly byte[] signingKey;
    private readonly Guid incarnation;

    internal RestorePublicationRaceAuthority(EventingArtifactFixture fixture, NativeCatalogBackupCapture capture)
    {
        original = NativeSerialization.Deserialize<ClusterBackupOwnerCut>(capture.Metadata.Span);
        var target = original.Owner with { PhysicalShardId = Guid.NewGuid(), Incarnation = Guid.NewGuid() };
        incarnation = target.Incarnation;
        mapping = new(ClusterRestoreOwnerMapping.CurrentVersion, original.Owner, target, Endpoints);
        signingKey = RandomNumberGenerator.GetBytes(fixture.Source.Store.Identity.SigningKey.Length);
    }

    internal long ExpectedPosition => checked(original.StorePosition
        + original.Partitions.Count(partition => partition.Roster.FirstSeenAppliedIndex > NoAppliedIndex)
        + RestoreCommits);

    internal StoreIdentity Restore(EventingArtifactFixture fixture, string backup, string target,
        Action? afterReconcile, CancellationToken token)
    {
        var work = fixture.Source.CreateReadWork(token);
        return ZoneTreeStore.RestoreCatalogBackup(backup, target, fixture.Source.StorageExecution,
            (transaction, identity, position, metadata) =>
            {
                fixture.Source.ReconcileCatalogRestore(transaction, identity, position, metadata,
                    [original], [mapping], work);
                afterReconcile?.Invoke();
            }, incarnation, signingKey, token);
    }

    internal async Task RequireIdentityAsync(StoreIdentity identity, StoreIdentity source)
    {
        await Assert.That(identity.Incarnation).IsEqualTo(incarnation);
        await Assert.That(identity.NodeId).IsNotEqualTo(source.NodeId);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(signingKey)).IsTrue();
        await Assert.That(identity.SigningKey.Span.SequenceEqual(source.SigningKey.Span)).IsFalse();
        await Assert.That(identity.DispatchPaused).IsTrue();
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(signingKey);
}
