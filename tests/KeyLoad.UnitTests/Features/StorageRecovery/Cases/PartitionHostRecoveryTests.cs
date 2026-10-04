using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>AC-REP-004/AUTH-002: recover verified images before validating the final protected catalog.</summary>
internal sealed class PartitionHostRecoveryTests
{
    private const int RecoveryAttempts = 2;

    /// <summary>A complete valid image with a missing protected principal fails closed after installation, without leaked locks.</summary>
    [Test]
    public async Task MissingProtectedPrincipalInVerifiedPendingImageRejectsHostAndSafeRetry()
        => await AssertRejectedAsync(PendingPrincipal.Missing);

    /// <summary>Changing a protected principal inside an otherwise valid image cannot bypass startup authorization validation.</summary>
    [Test]
    public async Task ModifiedProtectedPrincipalInVerifiedPendingImageRejectsHostAndSafeRetry()
        => await AssertRejectedAsync(PendingPrincipal.Modified);

    /// <summary>A valid protected principal survives pending-image installation and subsequent physical host reopening.</summary>
    [Test]
    public async Task ValidProtectedPrincipalInVerifiedPendingImageOpensAtRecoveredCut()
    {
        using var fixture = new PartitionHostRecoveryFixture();
        Assert.ThrowsExactly<VerifiedSnapshotInterruption>(() => fixture.StagePending(PendingPrincipal.Valid));
        await AssertPendingAsync(fixture);
        for (var attempt = 0; attempt < RecoveryAttempts; attempt++)
        {
            await using (var host = fixture.OpenHost())
            {
                await Assert.That(host.Database.LastApplied).IsEqualTo(PartitionHostRecoveryFixture.SnapshotCut);
                await Assert.That(host.Materializer.Log.State.CommittedIndex).IsEqualTo(PartitionHostRecoveryFixture.SnapshotCut);
                await Assert.That(host.Materializer.Snapshots.Current).IsEqualTo(fixture.Pending);
                await Assert.That(host.Database.Store.Identity.NodeId).IsEqualTo(fixture.TargetNodeId);
                await Assert.That(host.Database.Store.Identity.ReadGeneration).IsEqualTo(fixture.TargetGeneration + 1);
                var principal = host.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(
                    KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId)));
                await Assert.That(principal!.Revoked).IsFalse();
                await Assert.That(principal.ClusterAdministrator).IsTrue();
                await Assert.That(principal.PolicyEpoch).IsEqualTo(1);
            }
            await AssertInstalledAsync(fixture, PendingPrincipal.Valid);
        }
    }

    private static async Task AssertRejectedAsync(PendingPrincipal principal)
    {
        using var fixture = new PartitionHostRecoveryFixture();
        Assert.ThrowsExactly<VerifiedSnapshotInterruption>(() => fixture.StagePending(principal));
        await AssertPendingAsync(fixture);
        for (var attempt = 0; attempt < RecoveryAttempts; attempt++)
        {
            var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.OpenAndDisposeHostAsync);
            await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await AssertInstalledAsync(fixture, principal);
        }
    }

    private static async Task AssertPendingAsync(PartitionHostRecoveryFixture fixture)
    {
        using var stores = fixture.OpenStores();
        var incoming = Path.Combine(fixture.Options.DataDirectory, ReplicaProtocol.SnapshotDirectory, ReplicaProtocol.IncomingImage);
        var manifest = Path.Combine(fixture.Options.DataDirectory, ReplicaProtocol.SnapshotDirectory, ReplicaProtocol.IncomingManifest);
        await Assert.That(stores.Database.LastApplied).IsEqualTo(0);
        await Assert.That(stores.Canonical.Position).IsEqualTo(0);
        await Assert.That(stores.Log.State.Snapshot).IsNull();
        await Assert.That(stores.Canonical.VerifySnapshot(incoming).AppliedPosition).IsEqualTo(PartitionHostRecoveryFixture.SnapshotCut);
        await Assert.That(new FileInfo(incoming).Length).IsEqualTo(fixture.Pending.Length);
        await Assert.That(File.Exists(manifest)).IsTrue();
    }

    private static async Task AssertInstalledAsync(PartitionHostRecoveryFixture fixture, PendingPrincipal expected)
    {
        using var ownership = fixture.AcquireHostOwnership();
        using var stores = fixture.OpenStores();
        await Assert.That(stores.Database.LastApplied).IsEqualTo(PartitionHostRecoveryFixture.SnapshotCut);
        await Assert.That(stores.Log.State.CommittedIndex).IsEqualTo(PartitionHostRecoveryFixture.SnapshotCut);
        await Assert.That(stores.Log.State.Snapshot).IsEqualTo(fixture.Pending);
        await Assert.That(stores.Canonical.Identity.NodeId).IsEqualTo(fixture.TargetNodeId);
        await Assert.That(stores.Canonical.Identity.ReadGeneration).IsEqualTo(fixture.TargetGeneration + 1);
        var path = Path.Combine(fixture.Options.DataDirectory, ReplicaProtocol.SnapshotDirectory);
        var image = stores.Canonical.VerifySnapshot(Path.Combine(path, fixture.Pending.FileName));
        await Assert.That(image.Incarnation).IsEqualTo(fixture.Options.Incarnation);
        await Assert.That(image.AppliedPosition).IsEqualTo(PartitionHostRecoveryFixture.SnapshotCut);
        await Assert.That(File.Exists(Path.Combine(path, ReplicaProtocol.IncomingImage))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(path, ReplicaProtocol.IncomingManifest))).IsFalse();
        var principal = stores.Canonical.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId)));
        if (expected == PendingPrincipal.Missing)
        { await Assert.That(principal).IsNull(); }
        else
        { await Assert.That(principal!.Revoked).IsEqualTo(expected == PendingPrincipal.Modified); }
    }
}
