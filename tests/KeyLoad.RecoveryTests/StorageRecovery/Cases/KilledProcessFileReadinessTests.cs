
namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class KilledProcessFileReadinessTests
{
    private const string TreeDirectoryName = "tree";
    private const string MetadataWalPath = "tree/0.meta.wal";
    private const int ReadinessBoundSeconds = 5;
    private static readonly TimeSpan ReadinessBound = TimeSpan.FromSeconds(ReadinessBoundSeconds);
    private static readonly TimeSpan ObservationBound = ReadinessBound + TimeSpan.FromSeconds(1);

    [Test]
    [Arguments("owner.lock")]
    [Arguments("commands.wal")]
    [Arguments(MetadataWalPath)]
    public async Task AcStorage012_WaitsUntilEachRealStoreFileHolderReleases(string relativePath)
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            fixture.HoldFile(relativePath);
            var readiness = fixture.StartReadiness();
            await Assert.That(readiness.IsCompleted).IsFalse();
            fixture.ReleaseFile();
            await readiness.WaitAsync(ReadinessBound, TimeProvider.System);
            fixture.MarkReadinessObserved();
        });
    }

    [Test]
    public async Task AcStorage012_CancellationWhileMetadataWalIsHeldStopsReadinessWait()
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            fixture.HoldFile(MetadataWalPath);
            var readiness = fixture.StartReadiness();
            await Assert.That(readiness.IsCompleted).IsFalse();
            await fixture.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => readiness);
            fixture.MarkReadinessObserved();
        });
    }

    [Test]
    public async Task AcStorage012_PreCancelledTokenStopsBeforeAnyFileProbe()
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            await fixture.CancelAsync();
            var readiness = fixture.StartReadiness();
            await Assert.ThrowsAsync<OperationCanceledException>(() => readiness);
            fixture.MarkReadinessObserved();
        });
    }

    [Test]
    [NotInParallel]
    public async Task AcStorage012_PermanentMetadataWalLockFailsWithinReadinessBound()
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            fixture.HoldFile(MetadataWalPath);
            var clock = TimeProvider.System;
            var started = clock.GetTimestamp();
            var readiness = fixture.StartReadiness();
            await Assert.ThrowsExactlyAsync<IOException>(() => readiness);
            fixture.MarkReadinessObserved();
            await Assert.That(clock.GetElapsedTime(started)).IsGreaterThanOrEqualTo(ReadinessBound);
            await Assert.That(clock.GetElapsedTime(started)).IsLessThanOrEqualTo(ObservationBound);
        });
    }

    [Test]
    public async Task AcStorage012_MissingTreeDirectoryIsReadyWithoutCreatingMetadata()
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            fixture.MoveTreeAside();
            await KilledProcessFileReadiness.WaitAsync(fixture.Root, CancellationToken.None);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, TreeDirectoryName))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(fixture.Root, MetadataWalPath))).IsFalse();
            fixture.RestoreTree();
            await Assert.That(fixture.ReopenAndCommit()).IsTrue();
        });
    }

    [Test]
    public async Task AcStorage012_MissingMetadataFileIsReadyWithoutCreatingIt()
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            fixture.MoveMetadataWalAside();
            await KilledProcessFileReadiness.WaitAsync(fixture.Root, CancellationToken.None);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, TreeDirectoryName))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(fixture.Root, MetadataWalPath))).IsFalse();
            fixture.RestoreMetadataWal();
            await Assert.That(fixture.ReopenAndCommit()).IsTrue();
        });
    }

    [Test]
    [NotInParallel]
    [Arguments("owner.lock")]
    [Arguments("commands.wal")]
    public async Task AcStorage012_MissingRequiredOwnershipFileStillFails(string relativePath)
    {
        await using var fixture = new KilledProcessFileReadinessFixture();
        await fixture.RunAsync(async fixture =>
        {
            var missingPath = Path.Combine(fixture.Root, relativePath);
            File.Delete(missingPath);
            var clock = TimeProvider.System;
            var started = clock.GetTimestamp();
            var readiness = fixture.StartReadiness();
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => readiness);
            fixture.MarkReadinessObserved();
            await Assert.That(clock.GetElapsedTime(started)).IsGreaterThanOrEqualTo(ReadinessBound);
            await Assert.That(clock.GetElapsedTime(started)).IsLessThanOrEqualTo(ObservationBound);
            await Assert.That(File.Exists(missingPath)).IsFalse();
        });
    }
}
