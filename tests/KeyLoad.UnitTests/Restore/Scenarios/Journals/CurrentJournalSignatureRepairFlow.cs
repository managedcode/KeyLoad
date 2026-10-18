using System.Buffers.Binary;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentJournalSignatureRepairFlow
{
    private const ulong UnsupportedSignature = ulong.MaxValue;
    private const long CapturedPosition = 2L;
    private const string CliTargetSuffix = "-current-signature-cli";

    internal static async Task RunAsync(bool checkpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new NativeBackupCutFixture(checkpoint);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new CurrentJournalSignatureAuthority();
                await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture, authority, checkpoint, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(NativeBackupCutFixture fixture,
        CurrentJournalSignatureAuthority authority, bool checkpoint, CancellationToken token)
    {
        await Assert.That(fixture.Position).IsEqualTo(CapturedPosition);
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        await NativeBackupJournalRepairAssertions.OriginalSourceAsync(fixture, source);
        var original = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var originalSignature = BinaryPrimitives.ReadUInt64LittleEndian(original[MetadataTestContract.JournalFileName]);
        await Assert.That(originalSignature).IsEqualTo(checkpoint ? ZoneTreePersistenceFormat.CheckpointMagic : ZoneTreePersistenceFormat.JournalMagic);
        var cliTarget = fixture.Destination + CliTargetSuffix;
        var failures = new List<Exception>();
        Dictionary<string, byte[]>? faulted = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var damaged = original[MetadataTestContract.JournalFileName].ToArray();
            BinaryPrimitives.WriteUInt64LittleEndian(damaged, UnsupportedSignature);
            await fixture.RewriteJournalAsync(damaged);
            faulted = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            await CurrentJournalSignatureRefusal.RunAsync(fixture, authority, cliTarget, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => NativeBackupJournalRepairAssertions.OriginalSourceAndArchiveAsync(fixture,
            source, faulted ?? original, CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => NativeBackupJournalRepairAssertions.RepairArchiveAsync(fixture.Backup, original), failures);
        await ServerFailureObserver.ObserveAsync(() => NativeBackupJournalRepairAssertions.OriginalSourceAndArchiveAsync(fixture,
            source, original, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(() => CurrentJournalSignatureHealthy.RunAsync(fixture, authority, cliTarget, token), failures);
        await ServerFailureObserver.ObserveAsync(() => NativeBackupJournalRepairAssertions.OriginalSourceAndArchiveAsync(fixture,
            source, original, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
