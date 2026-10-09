using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

/// <summary>Real node-local owner for supporting admission; no successful RF3 capture claim.</summary>
internal sealed class ClusterBackupAdmissionNativeFixture : IAsyncDisposable
{
    internal const int AcceptedCaptures = 4;
    private const int InitialEpoch = 1;
    private const string BackupDirectory = "backups";
    private const string CaptureDirectory = "cluster-backups";
    private readonly PartitionHostRecoveryFixture files = new();
    private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
    internal PartitionHost Host { get; }
    internal NodeAdministration Administration { get; }
    internal AggregateException? ObservedShutdownFailure { get; set; }
    private readonly ReadOnlyMemory<byte> capability;
    private readonly string principal;
    private readonly IOptions<DatabaseLimits> limits = UnitExecutionOptions.DatabaseLimits();

    internal ClusterBackupAdmissionNativeFixture()
    {
        PartitionHost? opened = null;
        try
        {
            Host = opened = files.OpenHost();
            principal = Host.Database.Authenticate(files.Options.AdminKey, TimeProvider.System.GetUtcNow());
            var owner = new PhysicalShardRecord(files.Options.PhysicalShardId, files.Options.Incarnation,
                Host.Configuration.VoterIds, InitialEpoch);
            var request = new ClusterBackupOwnerRequest(ClusterBackupOwnerRequest.CurrentVersion,
                Guid.NewGuid(), owner, Host.Database.Store.Identity.NodeId);
            capability = NativeSerialization.Serialize(new ClusterBackupOwnerCapability(request,
                DatabaseEngine.IssueCredentialWitness(files.Options.AdminKey)));
            Administration = new(Host, new(UnitAdmissionOptions.Command(files.Options.CommandAdmission)),
                new(UnitAdmissionOptions.Http()), services,
                Options.Create(new ClusterBackupExecutionOptions { MaximumAdmissions = AcceptedCaptures }));
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (opened is not null)
            { ServerFailureObserver.Observe(() => opened.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            ServerFailureObserver.Observe(services.Dispose, failures);
            ServerFailureObserver.Observe(files.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<ClusterBackupOwnerReceipt> Capture(CancellationToken ct)
        => Administration.CaptureClusterBackupOwnerAsync(principal, capability,
            new ReadExecutionBudget(limits, TimeProvider.System, ct), ct);

    internal byte[] ReadCompleteImage() => Host.Database.Store.Read(view =>
    {
        var image = view.Scan([], limits.Value.MaxScanRecords);
        if (image.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, "The supporting native image exceeded its actual scan bound."); }
        return NativeSerialization.Serialize(image.Records);
    });

    internal async Task RequireHealthyOrdinaryBackupAsync(byte[] originalImage, CancellationToken ct)
    {
        var before = Host.Database.Store.Identity;
        var receipt = await Administration.BackupAsync(ct);
        var archive = Path.Combine(Host.DirectoryPath, BackupDirectory, receipt.Id);
        var verified = KeyLoad.Storage.ZoneTree.ZoneTreeStore.VerifyBackup(archive, UnitExecutionOptions.StorageExecution());
        await Assert.That(SHA256.HashData(NativeSerialization.Serialize(verified.Identity)))
            .IsEquivalentTo(SHA256.HashData(NativeSerialization.Serialize(before)));
        await Assert.That(verified.Position).IsEqualTo(receipt.Position);
        await Assert.That(ReadCompleteImage()).IsEquivalentTo(originalImage);
    }

    internal async Task RequireUnchangedAndColdAsync(byte[] originalImage)
    {
        await Assert.That(Directory.Exists(Path.Combine(Host.DirectoryPath, CaptureDirectory))).IsFalse();
        await Host.DisposeAsync();
        await using var cold = files.OpenHost();
        var actual = cold.Database.Store.Read(view =>
        {
            var page = view.Scan([], limits.Value.MaxScanRecords);
            if (page.HasMore)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, "The cold native image exceeded its actual scan bound."); }
            return NativeSerialization.Serialize(page.Records);
        });
        await Assert.That(actual).IsEquivalentTo(originalImage);
        await Assert.That(cold.Database.Authenticate(files.Options.AdminKey, TimeProvider.System.GetUtcNow()))
            .IsEqualTo(principal);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            { await Administration.DisposeAsync(); }
            catch (AggregateException original) when (ReferenceEquals(original, ObservedShutdownFailure)) { }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => Host.DisposeAsync().AsTask(), failures);
        Exception? infrastructureFailure = null;
        var filesJoined = false;
        try
        {
            try
            { services.Dispose(); }
            catch (Exception original)
            {
                infrastructureFailure = original;
                throw;
            }
            finally { files.Dispose(); filesJoined = true; }
        }
        catch (Exception terminal)
        {
            if (infrastructureFailure is not null)
            { failures.Add(infrastructureFailure); }
            if (infrastructureFailure is null || !filesJoined)
            { failures.Add(terminal); }
            // Earlier owner failures and both genuinely joined native cleanup failures retain order.
            throw new AggregateException(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
