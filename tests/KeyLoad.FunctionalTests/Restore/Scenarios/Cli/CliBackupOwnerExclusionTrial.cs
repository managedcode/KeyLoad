using System.Text.Json;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

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
            using var fixture = await BackupCutFixture.CreateAsync(token);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var destination = Path.Combine(Path.GetDirectoryName(fixture.Source)!, AdditionalBackupName);
        var archive = await BackupCutAssertions.ArchiveAsync(fixture.Backup, token);
        var source = BackupCutAssertions.Read(fixture.Source, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await BackupCutAssertions.SameCutAsync(source, Read(owner, token));
                await RefuseAsync(options, owner, fixture, destination, token);
                await RefuseAsync(options, owner, fixture, destination, token);
                await BackupCutAssertions.SameArchiveAsync(archive, fixture.Backup, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await HealthyAsync(options, fixture, destination, token);
        await BackupCutAssertions.SameCutAsync(source,
            BackupCutAssertions.Read(fixture.Source, token));
        await BackupCutAssertions.SameArchiveAsync(archive, fixture.Backup, token);
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
        await BackupCutAssertions.SameCutAsync(source, Read(owner, token));
    }

    private static async Task HealthyAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, string backup, CancellationToken token)
    {
        var created = await CliBackupRestoreProcess.RunAsync(options,
            ["backup", fixture.Source, backup], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(created);
        var archive = await BackupCutAssertions.ArchiveAsync(backup, token);
        await CliBackupRestoreAssertions.BackupLayoutAsync(archive);
        await Assert.That(archive[ZoneTreePersistenceFormat.IdentityFileName])
            .IsEquivalentTo(await File.ReadAllBytesAsync(Path.Combine(fixture.Source, ZoneTreePersistenceFormat.IdentityFileName), token),
                CollectionOrdering.Matching);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            ["restore", backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var restored = BackupCutAssertions.Read(fixture.Destination, token);
        await CliBackupRestoreAssertions.ReceiptAsync(result, restored.Identity);
        await BackupCutAssertions.RestoredAsync(fixture, restored.Identity, restored);
        await BackupCutAssertions.SameCutAsync(restored,
            BackupCutAssertions.Read(fixture.Destination, token));
        await BackupCutAssertions.SameCutAsync(restored,
            BackupCutAssertions.Read(fixture.Destination, token));
        await BackupCutAssertions.SameArchiveAsync(archive, backup, token);
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
