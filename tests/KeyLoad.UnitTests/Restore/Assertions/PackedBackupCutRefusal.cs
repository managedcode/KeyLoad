using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class PackedBackupCutRefusal
{
    private const string Detail = "The backup journal does not match its manifest cut.";
    private const string Code = "Corruption";
    private const string Type = "urn:keyload:error:Corruption";
    private const string Restore = "restore";
    private const string Staging = ".keyload-restore-*";
    private const int Status = 400;

    internal static async Task RunAsync(string root, string unpacked, string target, string cliTarget,
        Guid incarnation, byte[] signingKey, CancellationToken token)
    {
        var verified = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeBackupVerification.Verify(unpacked,
            UnitExecutionOptions.StorageExecution()));
        await FailureAsync(verified);
        var restored = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(unpacked, target,
            UnitExecutionOptions.StorageExecution(), incarnation, signingKey));
        await FailureAsync(restored);
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [Restore, unpacked, cliTarget], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEqualTo(string.Empty);
        var expected = Errors.Problem(ErrorCode.Corruption, Detail);
        await Assert.That(expected.Type).IsEqualTo(Type);
        await Assert.That(expected.Title).IsEqualTo(Code);
        await Assert.That(expected.ErrorCode).IsEqualTo(Code);
        await Assert.That(expected.Detail).IsEqualTo(Detail);
        await Assert.That(expected.StatusCode == Status).IsTrue();
        using var actual = JsonDocument.Parse(result.StandardError);
        await Assert.That(JsonElement.DeepEquals(actual.RootElement,
            JsonSerializer.SerializeToElement(expected, JsonDefaults.Options))).IsTrue();
        foreach (var path in new[] { target, cliTarget })
        {
            await Assert.That(File.Exists(path) || Directory.Exists(path)).IsFalse();
        }
        await Assert.That(Directory.EnumerateDirectories(root, Staging, SearchOption.AllDirectories).Any()).IsFalse();
    }

    private static async Task FailureAsync(KeyLoadException failure)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(Detail);
        await Assert.That(failure.StatusCode).IsEqualTo(Status);
        await Assert.That(failure.InnerException).IsNull();
    }
}
