using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

/// <summary>Owns supporting administration over a borrowed registered physical host; no CLUSTER capture claim.</summary>
internal sealed class ClusterBackupAdmissionNativeFixture : IAsyncDisposable
{
    internal const int AcceptedCaptures = 4;
    private const int InitialEpoch = 1;
    private const string BackupDirectory = "backups";
    private const string CaptureDirectory = "cluster-backups";
    private readonly Lock lifecycle = new();
    private readonly PartitionHostRecoveryFixture files;
    private readonly IOptions<DatabaseLimits> limits = UnitExecutionOptions.DatabaseLimits();
    private ServiceProvider? services;
    private NodeAdministration? administration;
    private Task? closing;
    private ReadOnlyMemory<byte> capability;
    internal PartitionHost Host
    {
        get;
    }
    internal string Principal
    {
        get;
        private set;
    } = null!;
    internal NodeAdministration Administration => administration
        ?? throw new ObjectDisposedException(nameof(ClusterBackupAdmissionNativeFixture));
    internal AggregateException? ObservedShutdownFailure
    {
        get; set;
    }

    private ClusterBackupAdmissionNativeFixture(PartitionHostRecoveryFixture files, PartitionHost host)
    {
        this.files = files;
        Host = host;
    }

    internal static async Task<ClusterBackupAdmissionNativeFixture> CreateAsync(
        PartitionHostRecoveryFixture files, PartitionHost host)
    {
        var fixture = new ClusterBackupAdmissionNativeFixture(files, host);
        try
        {
            fixture.Initialize();
            return fixture;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void Initialize()
    {
        services = new ServiceCollection().BuildServiceProvider();
        Principal = Host.Database.Authenticate(files.Options.AdminKey, TimeProvider.System.GetUtcNow());
        var owner = new PhysicalShardRecord(files.Options.PhysicalShardId, files.Options.Incarnation,
            Host.Configuration.VoterIds, InitialEpoch);
        var request = new ClusterBackupOwnerRequest(ClusterBackupOwnerRequest.CurrentVersion,
            Guid.NewGuid(), owner, Host.Database.Store.Identity.NodeId);
        capability = NativeSerialization.Serialize(new ClusterBackupOwnerCapability(request,
            DatabaseEngine.IssueCredentialWitness(files.Options.AdminKey)));
        administration = new(Host, new(UnitAdmissionOptions.Command(files.Options.CommandAdmission)),
            new(UnitAdmissionOptions.Http()), services,
            Options.Create(new ClusterBackupExecutionOptions { MaximumAdmissions = AcceptedCaptures }));
    }

    internal Task<ClusterBackupOwnerReceipt> Capture(CancellationToken ct)
        => Administration.CaptureClusterBackupOwnerAsync(Principal, capability,
            new ReadExecutionBudget(limits, TimeProvider.System, ct), ct);

    internal byte[] ReadCompleteImage() => Host.Database.Store.Read(view =>
    {
        var image = view.Scan([], limits.Value.MaxScanRecords);
        if (image.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The supporting native image exceeded its actual scan bound.");
        }
        return NativeSerialization.Serialize(image.Records);
    });

    internal async Task RequireHealthyOrdinaryBackupAsync(byte[] originalImage, CancellationToken ct)
    {
        var before = Host.Database.Store.Identity;
        var receipt = await Administration.BackupAsync(ct);
        var archive = Path.Combine(Host.DirectoryPath, BackupDirectory, receipt.Id);
        var verified = KeyLoad.Storage.ZoneTree.ZoneTreeStore.VerifyBackup(archive, UnitExecutionOptions.StorageExecution());
        await Assert.That(SHA256.HashData(NativeSerialization.Serialize(verified.Identity)).AsSpan()
            .SequenceEqual(SHA256.HashData(NativeSerialization.Serialize(before)))).IsTrue();
        await Assert.That(verified.Position).IsEqualTo(receipt.Position);
        await Assert.That(ReadCompleteImage().AsSpan().SequenceEqual(originalImage)).IsTrue();
    }

    internal async Task RequireNoClusterCaptureAsync()
        => await Assert.That(Directory.Exists(Path.Combine(Host.DirectoryPath, CaptureDirectory))).IsFalse();

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(closing ??= CloseCoreAsync());
        }
    }

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        if (administration is not null)
        {
            try
            {
                await administration.DisposeAsync();
            }
            catch (AggregateException original) when (ReferenceEquals(original, ObservedShutdownFailure))
            {
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        if (services is not null)
        {
            try
            {
                await services.DisposeAsync();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
