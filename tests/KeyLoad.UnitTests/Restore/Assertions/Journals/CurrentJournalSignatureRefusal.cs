using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentJournalSignatureRefusal
{
    private const string Detail = "The journal format is unsupported.";
    private const string Code = "FormatUnsupported";
    private const string Type = "urn:keyload:error:FormatUnsupported";
    private const int Status = 400;
    private const int FailedExit = 1;
    private const string Restore = "restore";
    private const string Staging = ".keyload-restore-*";

    internal static async Task RunAsync(NativeBackupCutFixture fixture, CurrentJournalSignatureAuthority authority,
        string cliTarget, CancellationToken token)
    {
        var verified = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.VerifyBackup(fixture.Backup,
            UnitExecutionOptions.StorageExecution()));
        await FailureAsync(verified);
        var restored = Assert.ThrowsExactly<KeyLoadException>(() => authority.Restore(fixture.Backup, fixture.Destination));
        await FailureAsync(restored);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.FormatUnsupported);
        var cli = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [Restore, fixture.Backup, cliTarget], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(cli);
        await Assert.That(cli.ExitCode).IsEqualTo(FailedExit);
        await Assert.That(cli.StandardOutput).IsEqualTo(string.Empty);
        var expected = Errors.Problem(ErrorCode.FormatUnsupported, Detail);
        await Assert.That(expected.Type).IsEqualTo(Type);
        await Assert.That(expected.Title).IsEqualTo(Code);
        await Assert.That(expected.ErrorCode).IsEqualTo(Code);
        await Assert.That(expected.Detail).IsEqualTo(Detail);
        await Assert.That(expected.StatusCode == Status).IsTrue();
        using var actual = JsonDocument.Parse(cli.StandardError);
        await Assert.That(JsonElement.DeepEquals(actual.RootElement,
            JsonSerializer.SerializeToElement(expected, JsonDefaults.Options))).IsTrue();
        foreach (var path in new[] { fixture.Destination, cliTarget })
        {
            await Assert.That(File.Exists(path) || Directory.Exists(path)).IsFalse();
        }
        var root = Path.GetDirectoryName(fixture.Source)!;
        await Assert.That(Directory.EnumerateDirectories(root, Staging, SearchOption.AllDirectories).Any()).IsFalse();
    }

    private static async Task FailureAsync(KeyLoadException failure)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(Detail);
        await Assert.That(failure.StatusCode).IsEqualTo(Status);
        await Assert.That(failure.InnerException).IsNull();
    }
}
