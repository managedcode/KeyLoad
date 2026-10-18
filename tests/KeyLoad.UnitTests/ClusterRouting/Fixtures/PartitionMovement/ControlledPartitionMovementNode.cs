using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns one real configured movement node through the shared original process-native owner.</summary>
internal sealed class ControlledPartitionMovementNode : IDisposable
{
    private const string DirectoryPrefix = "keyload-controlled-movement-node-";
    private const string GuidFormat = "N";
    private readonly string root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private ControlledPartitionMovementNativeNode native;
    private readonly IOptions<DatabaseLimits> limits;
    private bool closed;
    internal ControlledPartitionMovementNode(PhysicalShardRecord owner,
        long maximumFixtureEntries = ControlledPartitionMovementNativeJournal.SupportingHistoryEntries, DatabaseLimits? limits = null)
    {
        this.limits = UnitExecutionOptions.DatabaseLimits(limits);
        PhysicalOwner = owner;
        MaximumFixtureEntries = maximumFixtureEntries;
        native = new(root, owner, maximumFixtureEntries: maximumFixtureEntries, limits: this.limits);
    }
    internal DatabaseEngine Database => native.Database;
    /// <summary>Borrows the current owner without opening storage or changing its supporting operation limits.</summary>
    internal DatabaseEngine BorrowLifetimeIssuer(int maximumBytes)
        => new(Store, Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(Database.Limits with
            {
                MaxBatchBytes = maximumBytes
            }),
            global::KeyLoad.CrashHost.CrashExecutionOptions.DueWork(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.EventSource(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.Messaging(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.GraphExecution(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.ChangeFeedExecution(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.BlobExecution(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.NativeClaimsExecution(),
            global::KeyLoad.CrashHost.CrashExecutionOptions.TimeSeriesExecution(),
            Database.EvaluationClock, PhysicalOwner);

    internal ZoneTreeStore Store => native.Store;
    internal ControlledPartitionMovementNativeJournal Journal => native.Journal;
    internal string Root => native.Root;
    internal PhysicalShardRecord PhysicalOwner
    {
        get;
    }
    internal long MaximumFixtureEntries
    {
        get;
    }
    internal bool RetainRoot
    {
        get; set;
    }
    internal long AdditionalSeedEntries
    {
        get; set;
    }

    internal global::System.Collections.Immutable.ImmutableArray<ResourceDefinition> AdditionalSeedResources { get; set; } = [];
    internal void JoinForChild()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native.Dispose();
    }
    internal void OpenAfterChild()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native = new(root, PhysicalOwner, maximumFixtureEntries: MaximumFixtureEntries, limits: limits);
    }
    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native.Dispose();
        native = new(root, PhysicalOwner, maximumFixtureEntries: MaximumFixtureEntries, limits: limits);
    }
    internal KeyLoadException ReopenAfterExpectedCorruptApply()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        KeyLoadException? original = null;
        try
        {
            native.Dispose();
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption
            && failure.Message == KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveProtocol.Invalid)
        {
            original = failure;
        }
        if (original is null)
        {
            throw new InvalidOperationException("The original faulted apply worker did not retain its corruption.");
        }
        native = new(root, PhysicalOwner, maximumFixtureEntries: MaximumFixtureEntries, limits: limits);
        return original;
    }
    public void Dispose()
    {
        if (closed)
        {
            return;
        }
        closed = true;
        var failures = new List<Exception>();
        try
        {
            native.Dispose();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (!RetainRoot && failures.Count == 0 && Directory.Exists(root))
        {
            try
            {
                Directory.Delete(root, recursive: true);
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

