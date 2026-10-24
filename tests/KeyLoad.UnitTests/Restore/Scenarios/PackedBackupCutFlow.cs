using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class PackedBackupCutFlow
{
    private const string Artifact = "verified-container.ctg";
    private const string Unpacked = "verified-container-unpacked";
    private const string Target = "verified-container-restored";
    private const string CliTarget = "verified-container-cli-target";
    private const byte SigningByte = 7;

    internal static Task RunAsync(bool earlierJournal, CancellationToken token)
        => CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            var pair = await PackedBackupCutPair.CaptureAsync(fixture);
            var selected = earlierJournal ? pair.Second : pair.First;
            var donor = earlierJournal ? pair.First : pair.Second;
            var original = await BackupJournalRepairAssertions.ArchiveAsync(selected, token);
            var donorFiles = await BackupJournalRepairAssertions.ArchiveAsync(donor, token);
            var manifest = await MetadataTestFiles.ReadManifestAsync(selected);
            await Assert.That(manifest.Position).IsEqualTo(earlierJournal ? 2L : 1L);
            await Assert.That((await MetadataTestFiles.ReadManifestAsync(donor)).Position).IsEqualTo(earlierJournal ? 1L : 2L);
            var source = BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token);
            await CompleteAsync(fixture, source, selected, original, donor, donorFiles, manifest.Position, earlierJournal, token);
        }, token);

    private static async Task CompleteAsync(CliBackupRestoreFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source, string selected, Dictionary<string, byte[]> original,
        string donor, Dictionary<string, byte[]> donorFiles, long position, bool containsSecond, CancellationToken token)
    {
        var artifact = Path.Combine(fixture.Root, Artifact);
        var unpacked = Path.Combine(fixture.Root, Unpacked);
        var target = Path.Combine(fixture.Root, Target);
        var cliTarget = Path.Combine(fixture.Root, CliTarget);
        var incarnation = Guid.NewGuid();
        var key = Enumerable.Repeat(SigningByte, ZoneTreePersistenceFormat.SigningKeyBytes).ToArray();
        var failures = new List<Exception>();
        Dictionary<string, byte[]>? mixed = null;
        byte[]? packed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await PackedBackupCutTransfer.MixAsync(selected, donorFiles, token);
            mixed = await BackupJournalRepairAssertions.ArchiveAsync(selected, token);
            await PackedBackupCutTransfer.PackAsync(selected, artifact, mixed, cli: false, token);
            packed = await File.ReadAllBytesAsync(artifact, token);
            await PackedBackupCutTransfer.UnpackAsync(artifact, unpacked, mixed, cli: false, token);
            await PackedBackupCutRefusal.RunAsync(fixture.Root, unpacked, target, cliTarget, incarnation, key, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupCutConservation.RunAsync(fixture, source,
            selected, mixed ?? original, donor, donorFiles, artifact, packed, CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupCutTransfer.RepairAsync(selected, original), failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupCutConservation.RunAsync(fixture, source,
            selected, original, donor, donorFiles, artifact, packed, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            File.Delete(artifact);
            Directory.Delete(unpacked, recursive: true);
            await PackedBackupCutTransfer.PackAsync(selected, artifact, original, cli: true, token);
            packed = await File.ReadAllBytesAsync(artifact, token);
            await PackedBackupCutTransfer.UnpackAsync(artifact, unpacked, original, cli: true, token);
            await PackedBackupCutHealthy.RunAsync(fixture, unpacked, target, cliTarget, incarnation, key, position, containsSecond, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupCutConservation.RunAsync(fixture, source,
            selected, original, donor, donorFiles, artifact, packed, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
