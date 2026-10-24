using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree.IO;
using KeyLoad.UnitTests.OrleansExecution;
using P = KeyLoad.UnitTests.BackupRestore.ArchiveEntryProtocol;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class ArchiveEntryRefusal
{
    internal static async Task RunAsync(ArchiveEntryFixture fixture, string file,
        Dictionary<string, byte[]> archive, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }
        var path = Path.Combine(fixture.Backup, file);
        var retained = Path.Combine(fixture.Root, P.HiddenPrefix + file);
        File.Move(path, retained);
        RequestCqrsProbeFifo.Create(path);
        var original = OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path);
        var mode = ReadUnixMode(path);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await fixture.ChildAsync([P.RestoreCommand, fixture.Backup, fixture.Target], token);
            await CliBackupRestoreAssertions.RejectedProcessAsync(result);
            await Assert.That(result.ExitCode).IsEqualTo(P.FailedExit);
            await Assert.That(result.StandardOutput).IsEmpty();
            var code = file == P.Journal ? ErrorCode.Corruption : ErrorCode.FormatUnsupported;
            var detail = file == P.Manifest ? P.ManifestUnsupported : file == P.Identity ? P.IdentityUnsupported : P.JournalInvalid;
            using var problem = JsonDocument.Parse(result.StandardError);
            await Assert.That(JsonElement.DeepEquals(problem.RootElement,
                JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options))).IsTrue();
            await BackupRequiredFileAccessAssertions.NoPublicationAsync(fixture.Backup, fixture.Target, existingEmpty: false);
            var current = OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path);
            await Assert.That(current).IsEqualTo(original);
            await Assert.That(ReadUnixMode(path)).IsEqualTo(mode);
            await Assert.That((await File.ReadAllBytesAsync(retained, token)).AsSpan().SequenceEqual(archive[file])).IsTrue();
            foreach (var name in P.Files.Where(name => name != file))
            {
                await Assert.That((await File.ReadAllBytesAsync(Path.Combine(fixture.Backup, name), token)).AsSpan()
                    .SequenceEqual(archive[name])).IsTrue();
            }
            await Assert.That(Directory.GetFileSystemEntries(fixture.Backup).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(P.Files.Order(StringComparer.Ordinal))).IsTrue();
        }, failures);
        if (fixture.ChildClosed)
        {
            ServerFailureObserver.Observe(() => File.Delete(path), failures);
            ServerFailureObserver.Observe(() => File.Move(retained, path), failures);
        }
        else
        {
            fixture.RetainRoot();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static UnixFileMode ReadUnixMode(string path)
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException();
        }
        return File.GetUnixFileMode(path);
    }
}
