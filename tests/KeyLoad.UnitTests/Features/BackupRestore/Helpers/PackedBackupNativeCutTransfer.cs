using KeyLoad.Artifacts;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class PackedBackupNativeCutTransfer
{
    private const string Journal = "commands.wal";
    private const int PieceBytes = 1_024;
    private const int FirstPiece = 1;
    private const string PieceEnvironment = "KEYLOAD_BACKUP__PIECEBYTES";
    private const string PieceValue = "1024";
    private const string Pack = "pack-backup";
    private const string Unpack = "unpack-backup";

    internal static async Task MixAsync(string selected, Dictionary<string, byte[]> donor, CancellationToken token)
    {
        var manifest = await MetadataTestFiles.ReadManifestAsync(selected);
        await File.WriteAllBytesAsync(Path.Combine(selected, Journal), donor[Journal], token);
        await MetadataTestFiles.UpdateManifestFileAsync(selected, Journal);
        await Assert.That((await MetadataTestFiles.ReadManifestAsync(selected)).Position).IsEqualTo(manifest.Position);
    }

    internal static async Task PackAsync(string source, string artifact, Dictionary<string, byte[]> files,
        bool cli, CancellationToken token)
    {
        if (cli)
        {
            var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
                [Pack, source, artifact], token, new Dictionary<string, string> { [PieceEnvironment] = PieceValue });
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        }
        else
        {
            BackupArtifact.Pack(source, artifact, PieceBytes);
        }
        var actual = BackupArtifact.Inspect(artifact);
        var expected = files.Select(row => (Name: row.Key, Bytes: row.Value.LongLength,
            Pieces: Math.Max(FirstPiece, checked((row.Value.Length + PieceBytes - FirstPiece) / PieceBytes))))
            .OrderBy(row => row.Name, StringComparer.Ordinal);
        await Assert.That(actual.OrderBy(row => row.Name, StringComparer.Ordinal))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    internal static async Task UnpackAsync(string artifact, string unpacked, Dictionary<string, byte[]> files,
        bool cli, CancellationToken token)
    {
        if (cli)
        {
            var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
                [Unpack, artifact, unpacked], token);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        }
        else
        {
            BackupArtifact.Unpack(artifact, unpacked);
        }
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(files, unpacked, token);
    }

    internal static async Task RepairAsync(string selected, Dictionary<string, byte[]> original)
    {
        foreach (var row in original)
        {
            await File.WriteAllBytesAsync(Path.Combine(selected, row.Key), row.Value, CancellationToken.None);
        }
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(original, selected, CancellationToken.None);
    }
}
