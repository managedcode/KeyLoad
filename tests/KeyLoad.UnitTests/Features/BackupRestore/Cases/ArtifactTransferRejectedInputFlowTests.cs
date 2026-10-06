using KeyLoad.Artifacts;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class ArtifactTransferRejectedInputFlowTests
{
    private const int InvalidPieceBytes = 1_023;
    private const string PieceBytesParameterName = "pieceBytes";

    [Test]
    public async Task AcBackup001RejectedArtifactInputsPreserveNativeStateThenHealthyRestoreSucceeds()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            ArtifactTransferFlowFixture.SeedAndCreateBackup(fixture);
            var source = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, token);
            var backup = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, token);
            await RejectDirectoryTransferAsync(fixture, source, backup, token);
            await RejectInvalidPieceSizeAsync(fixture);
            await ArtifactTransferFlowAssertions.AssertUnpublishedAndPreservedAsync(fixture, source, backup, token);
            await ArtifactTransferFlowAssertions.RunHealthyArchiveRestoreAsync(fixture, source, backup, token);
        }, token);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task RejectDirectoryTransferAsync(CliBackupRestoreFixture fixture,
        IReadOnlyDictionary<string, byte[]> source, IReadOnlyDictionary<string, byte[]> backup,
        CancellationToken cancellationToken)
    {
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            ArtifactTransfer.CopyToFileStorageAsync(fixture.BackupDirectory, fixture.CopiedArtifactDirectory,
                cancellationToken));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
        await ArtifactTransferFlowAssertions.AssertUnpublishedAndPreservedAsync(
            fixture, source, backup, cancellationToken);
    }

    private static async Task RejectInvalidPieceSizeAsync(CliBackupRestoreFixture fixture)
    {
        var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            BackupArtifact.Pack(fixture.BackupDirectory, fixture.ArtifactPath, InvalidPieceBytes));
        await Assert.That(failure!.ParamName).IsEqualTo(PieceBytesParameterName);
    }
}
