using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

/// <summary>AC-REP-004 / AC-STORAGE-012: replica restart waits for real canonical and replica file ownership to clear.</summary>
internal sealed class ReplicaFileReadinessTests
{
    private const int ReadinessBoundSeconds = 5;
    private static readonly TimeSpan ReadinessBound = TimeSpan.FromSeconds(ReadinessBoundSeconds);
    private static readonly TimeSpan ObservationBound = ReadinessBound + TimeSpan.FromSeconds(1);

    /// <summary>Either real closed target ZoneTree metadata WAL keeps the owning wait pending until released.</summary>
    [Test]
    [Arguments("canonical")]
    [Arguments("replica")]
    public async Task AcRep004_WaitsForRealTargetMetadataWalRelease(string store)
    {
        await using var fixture = new ReplicaFileReadinessFixture();
        await fixture.RunAsync(async () =>
        {
            fixture.HoldMetadataWal(store);
            var readiness = fixture.StartReadiness();
            await Assert.That(readiness.IsCompleted).IsFalse();
            fixture.ReleaseMetadataWal();
            await fixture.ObserveReadinessAsync();
            await Assert.That(readiness.IsCompletedSuccessfully).IsTrue();
        });
    }

    /// <summary>A real source-side owner, journal, or metadata WAL keeps SnapshotVerified readiness pending until release.</summary>
    [Test]
    [Arguments("canonical", "owner.lock")]
    [Arguments("canonical", "commands.wal")]
    [Arguments("canonical", "tree/0.meta.wal")]
    [Arguments("replica", "owner.lock")]
    [Arguments("replica", "commands.wal")]
    [Arguments("replica", "tree/0.meta.wal")]
    public async Task AcRep004_SnapshotVerifiedWaitsForRealSourceFiles(string store, string file)
    {
        await using var fixture = new ReplicaFileReadinessFixture(ReplicaCrashBoundary.SnapshotVerified);
        await fixture.RunAsync(async () =>
        {
            fixture.HoldSourceFile(store, file);
            var readiness = fixture.StartReadiness(ReplicaCrashBoundary.SnapshotVerified);
            await Assert.That(readiness.IsCompleted).IsFalse();
            fixture.ReleaseMetadataWal();
            await fixture.ObserveReadinessAsync();
            await Assert.That(readiness.IsCompletedSuccessfully).IsTrue();
        });
    }

    /// <summary>A held target metadata WAL keeps the real ownership waiter cancellable.</summary>
    [Test]
    public async Task AcRep004_CancellationStopsHeldTargetMetadataReadiness()
    {
        await using var fixture = new ReplicaFileReadinessFixture();
        await fixture.RunAsync(async () =>
        {
            fixture.HoldMetadataWal("canonical");
            var readiness = fixture.StartReadiness();
            await Assert.That(readiness.IsCompleted).IsFalse();
            await fixture.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(fixture.ObserveReadinessAsync);
            await Assert.That(readiness.IsCanceled).IsTrue();
        });
    }

    /// <summary>A pre-cancelled token stops an unlocked target wait before a successful probe can return.</summary>
    [Test]
    public async Task AcRep004_PreCancelledUnlockedReadinessWaitStopsImmediately()
    {
        await using var fixture = new ReplicaFileReadinessFixture();
        await fixture.RunAsync(async () =>
        {
            await fixture.CancelAsync();
            var readiness = fixture.StartReadiness();
            await Assert.ThrowsAsync<OperationCanceledException>(fixture.ObserveReadinessAsync);
            await Assert.That(readiness.IsCanceled).IsTrue();
        });
    }

    /// <summary>A permanent real metadata holder preserves the existing five-second readiness failure bound.</summary>
    [Test]
    [NotInParallel]
    public async Task AcRep004_PermanentTargetMetadataHolderFailsWithinExistingBound()
    {
        await using var fixture = new ReplicaFileReadinessFixture();
        await fixture.RunAsync(async () =>
        {
            fixture.HoldMetadataWal("replica");
            var clock = TimeProvider.System;
            var started = clock.GetTimestamp();
            var readiness = fixture.StartReadiness();
            await Assert.ThrowsExactlyAsync<IOException>(fixture.ObserveReadinessAsync);
            await Assert.That(readiness.IsFaulted).IsTrue();
            await Assert.That(clock.GetElapsedTime(started)).IsGreaterThanOrEqualTo(ReadinessBound);
            await Assert.That(clock.GetElapsedTime(started)).IsLessThanOrEqualTo(ObservationBound);
        });
    }
}
