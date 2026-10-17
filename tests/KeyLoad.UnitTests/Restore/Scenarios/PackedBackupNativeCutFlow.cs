using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class PackedBackupNativeCutFlow
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
            var original = await NativeBackupJournalRepairAssertions.ArchiveAsync(selected, token);
            var donorFiles = await NativeBackupJournalRepairAssertions.ArchiveAsync(donor, token);
            var manifest = await MetadataTestFiles.ReadManifestAsync(selected);
            await Assert.That(manifest.Position).IsEqualTo(earlierJournal ? 2L : 1L);
            await Assert.That((await MetadataTestFiles.ReadManifestAsync(donor)).Position).IsEqualTo(earlierJournal ? 1L : 2L);
            var source = NativeBackupJournalRepairAssertions.Read(fixture.SourceDirectory, token);
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
            await PackedBackupNativeCutTransfer.MixAsync(selected, donorFiles, token);
            mixed = await NativeBackupJournalRepairAssertions.ArchiveAsync(selected, token);
            await PackedBackupNativeCutTransfer.PackAsync(selected, artifact, mixed, cli: false, token);
            packed = await File.ReadAllBytesAsync(artifact, token);
            await PackedBackupNativeCutTransfer.UnpackAsync(artifact, unpacked, mixed, cli: false, token);
            await PackedBackupNativeCutRefusal.RunAsync(fixture.Root, unpacked, target, cliTarget, incarnation, key, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupNativeCutConservation.RunAsync(fixture, source,
            selected, mixed ?? original, donor, donorFiles, artifact, packed, CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupNativeCutTransfer.RepairAsync(selected, original), failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupNativeCutConservation.RunAsync(fixture, source,
            selected, original, donor, donorFiles, artifact, packed, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            File.Delete(artifact);
            Directory.Delete(unpacked, recursive: true);
            await PackedBackupNativeCutTransfer.PackAsync(selected, artifact, original, cli: true, token);
            packed = await File.ReadAllBytesAsync(artifact, token);
            await PackedBackupNativeCutTransfer.UnpackAsync(artifact, unpacked, original, cli: true, token);
            await PackedBackupNativeCutHealthy.RunAsync(fixture, unpacked, target, cliTarget, incarnation, key, position, containsSecond, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => PackedBackupNativeCutConservation.RunAsync(fixture, source,
            selected, original, donor, donorFiles, artifact, packed, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
