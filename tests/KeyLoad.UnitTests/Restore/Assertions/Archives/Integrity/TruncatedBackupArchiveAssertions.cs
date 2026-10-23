using System.Text.Json;
using KeyLoad.Storage.ZoneTree.Restore;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class TruncatedBackupArchiveAssertions
{
    private const string StagePrefix = ".keyload-unpack-";
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";
    private const string UnsupportedCode = "FormatUnsupported";
    private const string MissingManifest = "The backup manifest is unsupported.";
    private const string MissingRefusal = "The original truncated archive unexpectedly published.";

    internal static async Task<Exception> RefusalAsync(CliBackupRestoreFixture fixture, string output,
        bool emptyDestination, UnixFileMode? mode, byte[] truncated, CancellationToken token)
    {
        Exception? original = null;
        try
        {
            BackupArtifact.Unpack(fixture.ArtifactPath, output);
        }
        catch (Cartograph.Format.CartographFormatException failure) { original = failure; }
        await Assert.That(original).IsNotNull();
        var retained = original ?? throw new InvalidOperationException(MissingRefusal);
        await Console.Error.WriteLineAsync(JsonSerializer.Serialize(new
        {
            exceptionType = retained.GetType().FullName,
            hResult = retained.HResult,
            text = retained.ToString()
        }, JsonDefaults.Options));
        await DestinationAsync(output, emptyDestination, mode);
        await NoStageAsync(fixture.Root);
        await ArchiveAsync(fixture.ArtifactPath, truncated, token);
        return retained;
    }

    internal static async Task DestinationAsync(string path, bool emptyDestination, UnixFileMode? mode)
    {
        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(Directory.Exists(path)).IsEqualTo(emptyDestination);
        if (emptyDestination)
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(path).Any()).IsFalse();
            if (!OperatingSystem.IsWindows() && mode is not null)
            {
                await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(mode.Value);
            }
        }
    }

    internal static async Task RejectedRestoreAsync(CliBackupRestoreProcessResult result, string destination)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
        await Assert.That(File.Exists(destination) || Directory.Exists(destination)).IsFalse();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(UnsupportedCode);
        await Assert.That(problem.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(MissingManifest);
    }

    internal static async Task ArchiveAsync(string path, byte[] expected, CancellationToken token)
    {
        var actual = await File.ReadAllBytesAsync(path, token);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    internal static async Task NoStageAsync(string root)
        => await Assert.That(Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName)
            .Where(name => name?.StartsWith(StagePrefix, StringComparison.Ordinal) == true).ToArray()).IsEmpty();
}
