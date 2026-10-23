using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutRefusal
{
    private const string Detail = "The backup journal does not match its manifest cut.";
    private const string Restore = "restore";
    private const string Staging = ".keyload-restore-*";
    private const string CliTarget = "mixed-cut-cli-target";
    private const string CorruptionCode = "Corruption";
    private const string CorruptionType = "urn:keyload:error:Corruption";
    private const int CorruptionStatus = (int)System.Net.HttpStatusCode.BadRequest;

    internal static async Task RunAsync(EventingArtifactFixture fixture,
        VerifiedBackupCutSnapshot selected, string target, CancellationToken token)
    {
        var verified = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.VerifyBackup(selected.Path, fixture.Source.StorageExecution, token));
        await RequireFailureAsync(verified);
        var restored = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(selected.Path, target, fixture.Source.StorageExecution, cancellationToken: token));
        await RequireFailureAsync(restored);
        await RequireNoPublicationAsync(fixture.Root, target);
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cliTarget = Path.Combine(fixture.Root, CliTarget);
        var cli = await CliBackupRestoreProcess.RunAsync(options, [Restore, selected.Path, cliTarget], token);
        await RequireCliFailureAsync(cli);
        await RequireNoPublicationAsync(fixture.Root, cliTarget);
    }

    private static async Task RequireFailureAsync(KeyLoadException failure)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(Detail);
        await Assert.That(failure.StatusCode).IsEqualTo(CorruptionStatus);
        await Assert.That(failure.InnerException).IsNull();
    }

    private static async Task RequireCliFailureAsync(CliBackupRestoreProcessResult result)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEqualTo(string.Empty);
        var expected = Errors.Problem(ErrorCode.Corruption, Detail);
        await Assert.That(expected.Type).IsEqualTo(CorruptionType);
        await Assert.That(expected.Title).IsEqualTo(CorruptionCode);
        await Assert.That(expected.ErrorCode).IsEqualTo(CorruptionCode);
        await Assert.That(expected.Detail).IsEqualTo(Detail);
        await Assert.That(expected.StatusCode == CorruptionStatus).IsTrue();
        using var actual = JsonDocument.Parse(result.StandardError);
        var complete = JsonSerializer.SerializeToElement(expected, JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(actual.RootElement, complete)).IsTrue();
    }

    private static async Task RequireNoPublicationAsync(string root, string target)
    {
        await Assert.That(Directory.Exists(target)).IsFalse();
        await Assert.That(File.Exists(target)).IsFalse();
        await Assert.That(Directory.EnumerateDirectories(root, Staging, SearchOption.AllDirectories).Any()).IsFalse();
    }
}
