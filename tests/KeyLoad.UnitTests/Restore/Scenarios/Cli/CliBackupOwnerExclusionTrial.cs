using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CliBackupOwnerExclusionTrial
{
    private const int MaximumFixtureRows = 128;
    private const string AdditionalBackupName = "cli-owner-exclusion-backup";
    private const int RejectedCliExitCode = 1;
    private const int UnavailableSourceStatusCode = 503;
    private const string UnavailableSourceType = "urn:keyload:error:RecoveryRequired";
    private const string UnavailableSourceCode = "RecoveryRequired";
    private const string UnavailableSourceDetail = "The offline backup source could not be opened.";

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new NativeBackupCutFixture();
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(NativeBackupCutFixture fixture, CancellationToken token)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var destination = Path.Combine(Path.GetDirectoryName(fixture.Source)!, AdditionalBackupName);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await NativeBackupJournalRepairAssertions.SameCutAsync(source, Read(owner, token));
                await RefuseAsync(options, owner, fixture, destination, token);
                await RefuseAsync(options, owner, fixture, destination, token);
                await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await HealthyAsync(options, fixture, destination, token);
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAsync(IOptions<TestExecutionOptions> options, ZoneTreeStore owner,
        NativeBackupCutFixture fixture, string destination, CancellationToken token)
    {
        var source = Read(owner, token);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            ["backup", fixture.Source, destination], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(RejectedCliExitCode);
        await Assert.That(result.StandardError.Length <= options.Value.CleanupOutputCharacters).IsTrue();
        var problem = JsonSerializer.Deserialize<Problem>(result.StandardError, JsonDefaults.Options);
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Type).IsEqualTo(UnavailableSourceType);
        await Assert.That(problem.Title).IsEqualTo(UnavailableSourceCode);
        await Assert.That(problem.ErrorCode).IsEqualTo(UnavailableSourceCode);
        await Assert.That(problem.Detail).IsEqualTo(UnavailableSourceDetail);
        await Assert.That(problem.StatusCode).IsEqualTo(UnavailableSourceStatusCode);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(Directory.Exists(destination) || File.Exists(destination)).IsFalse();
        await NativeBackupJournalRepairAssertions.SameCutAsync(source, Read(owner, token));
    }

    private static async Task HealthyAsync(IOptions<TestExecutionOptions> options,
        NativeBackupCutFixture fixture, string backup, CancellationToken token)
    {
        var created = await CliBackupRestoreProcess.RunAsync(options,
            ["backup", fixture.Source, backup], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(created);
        var verified = ZoneTreeStore.VerifyBackup(backup, UnitExecutionOptions.StorageExecution());
        await Assert.That(verified.Position).IsEqualTo(fixture.Position);
        await Assert.That(NativeSerialization.Serialize(verified.Identity)
            .SequenceEqual(NativeSerialization.Serialize(fixture.Identity))).IsTrue();
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(backup, token);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            ["restore", backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var restored = NativeBackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, restored.Identity);
        await NativeBackupJournalRepairAssertions.RestoredAsync(fixture, restored.Identity, restored);
        await NativeBackupJournalRepairAssertions.SameCutAsync(restored,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameCutAsync(restored,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, backup, token);
    }

    private static (StoreIdentity Identity, long Position, string[] Rows) Read(ZoneTreeStore owner,
        CancellationToken token)
        => owner.Read(view =>
        {
            token.ThrowIfCancellationRequested();
            var page = view.Scan([], MaximumFixtureRows);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The original held-owner fixture exceeded its complete-row bound.");
            }
            return (owner.Identity, owner.Position, page.Records.Select(row =>
                Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
        });
}
