using System.Globalization;
using KeyLoad.Storage.ZoneTree.Restore;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class BackupPieceBoundaryFlow
{
    private const string JournalName = "commands.wal";
    private const string PackCommand = "pack-backup";
    private const string UnpackCommand = "unpack-backup";
    private const string RestoreCommand = "restore";
    private const string InvalidPieceValue = "1023";
    private const int BoundaryStep = 1;
    private const int ExactBoundary = 0;
    private const int SplitPieces = 2;
    private const int SinglePiece = 1;
    private const int MinimumPiece = 1024;
    private const int InvalidPiece = 1023;
    private const string PieceParameter = "pieceBytes";
    private const string Below = "piece-below";
    private const string Exact = "piece-exact";
    private const string Above = "piece-above";
    private const string RuntimeArtifactSuffix = ".execution.ctg";
    private const string CliArtifactSuffix = ".cli.ctg";
    private const string DirectorySuffix = "-execution";
    private const string CliDirectorySuffix = "-cli";

    internal static async Task RunAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, IReadOnlyDictionary<string, byte[]> files, byte[] key,
        string payload, string environment, CancellationToken token)
    {
        var original = BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token);
        var length = files[JournalName].Length;
        await Assert.That(length).IsGreaterThan(MinimumPiece);
        foreach (var boundary in new[] { (-BoundaryStep, SplitPieces, Below),
            (ExactBoundary, SinglePiece, Exact), (BoundaryStep, SinglePiece, Above) })
        {
            var piece = checked(length + boundary.Item1);
            await RuntimeAsyncCore(options, fixture, files, key, payload, piece, boundary.Item2, boundary.Item3, token);
            await CliAsync(options, fixture, files, key, payload, environment, piece, boundary.Item2, boundary.Item3, token);
            await BackupJournalRepairAssertions.SameCutAsync(original,
                BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
        }
    }

    private static async Task RuntimeAsyncCore(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, IReadOnlyDictionary<string, byte[]> files, byte[] key, string payload,
        int piece, int journalPieces, string label, CancellationToken token)
    {
        var artifact = fixture.ArtifactPath + label + RuntimeArtifactSuffix;
        var unpacked = fixture.UnpackedDirectory + label + DirectorySuffix;
        var restored = fixture.RestoredDirectory + label + DirectorySuffix;
        var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            BackupArtifact.Pack(fixture.BackupDirectory, artifact, InvalidPiece));
        await Assert.That(failure!.ParamName).IsEqualTo(PieceParameter);
        await Assert.That(File.Exists(artifact)).IsFalse();
        await CliBackupRestoreAssertions.FilesEqualAsync(files, fixture.BackupDirectory, token);
        BackupArtifact.Pack(fixture.BackupDirectory, artifact, piece);
        await BackupPieceBoundaryAssertions.ArchiveAsync(artifact, files, piece, journalPieces);
        var archive = await File.ReadAllBytesAsync(artifact, token);
        BackupArtifact.Unpack(artifact, unpacked);
        await CliBackupRestoreAssertions.FilesEqualAsync(files, unpacked, token);
        await RestoreAsync(options, fixture, unpacked, restored, key, payload, token);
        await BackupPieceBoundaryAssertions.UnchangedAsync(artifact, archive, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(files, fixture.BackupDirectory, token);
    }

    private static async Task CliAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, IReadOnlyDictionary<string, byte[]> files, byte[] key, string payload,
        string environment, int piece, int journalPieces, string label, CancellationToken token)
    {
        var artifact = fixture.ArtifactPath + label + CliArtifactSuffix;
        var unpacked = fixture.UnpackedDirectory + label + CliDirectorySuffix;
        var restored = fixture.RestoredDirectory + label + CliDirectorySuffix;
        var rejected = await CliBackupRestoreProcess.RunAsync(options,
            [PackCommand, fixture.BackupDirectory, artifact], token,
            new Dictionary<string, string> { [environment] = InvalidPieceValue });
        await BackupPieceBoundaryAssertions.RejectedAsync(rejected, artifact);
        await CliBackupRestoreAssertions.FilesEqualAsync(files, fixture.BackupDirectory, token);
        await fixture.AssertSeedRemainsAsync();
        var packed = await CliBackupRestoreProcess.RunAsync(options,
            [PackCommand, fixture.BackupDirectory, artifact], token,
            new Dictionary<string, string> { [environment] = piece.ToString(CultureInfo.InvariantCulture) });
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(packed);
        await BackupPieceBoundaryAssertions.ArchiveAsync(artifact, files, piece, journalPieces);
        var archive = await File.ReadAllBytesAsync(artifact, token);
        var extracted = await CliBackupRestoreProcess.RunAsync(options, [UnpackCommand, artifact, unpacked], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(extracted);
        await CliBackupRestoreAssertions.FilesEqualAsync(files, unpacked, token);
        await RestoreAsync(options, fixture, unpacked, restored, key, payload, token);
        await BackupPieceBoundaryAssertions.UnchangedAsync(artifact, archive, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(files, fixture.BackupDirectory, token);
    }

    private static async Task RestoreAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, string unpacked, string restored, byte[] key, string payload,
        CancellationToken token)
    {
        var result = await CliBackupRestoreProcess.RunAsync(options, [RestoreCommand, unpacked, restored], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, result, restored);
        await BackupPieceBoundaryCold.RunAsync(fixture, result, restored, key, payload, token);
    }
}
