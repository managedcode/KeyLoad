using System.Text.Json;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class CliBackupOwnerExclusionTrial
{
    private const int MaximumFixtureRows = 128;
    private const string AdditionalBackupName = "cli-owner-exclusion-backup";
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
            using var fixture = new BackupCutFixture();
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var destination = Path.Combine(Path.GetDirectoryName(fixture.Source)!, AdditionalBackupName);
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await BackupJournalRepairAssertions.SameCutAsync(source, Read(owner, token));
                await RefuseAsync(options, owner, fixture, destination, token);
                await RefuseAsync(options, owner, fixture, destination, token);
                await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await HealthyAsync(options, fixture, destination, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAsync(IOptions<TestExecutionOptions> options, ZoneTreeStore owner,
        BackupCutFixture fixture, string destination, CancellationToken token)
    {
        var source = Read(owner, token);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            ["backup", fixture.Source, destination], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
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
        await BackupJournalRepairAssertions.SameCutAsync(source, Read(owner, token));
    }

    private static async Task HealthyAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, string backup, CancellationToken token)
    {
        var created = await CliBackupRestoreProcess.RunAsync(options,
            ["backup", fixture.Source, backup], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(created);
        var verified = ZoneTreeStore.VerifyBackup(backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(fixture.Position);
        await Assert.That(OrleansSerialization.Serialize(verified.Identity)
            .SequenceEqual(OrleansSerialization.Serialize(fixture.Identity))).IsTrue();
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(backup, token);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            ["restore", backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var restored = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, restored.Identity);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, restored.Identity, restored);
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, backup, token);
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
