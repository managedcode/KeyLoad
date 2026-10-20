using System.Runtime.ExceptionServices;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class TransactionalReadViewState : IDisposable
{
    internal const int MaximumRows = 4_096;
    internal const long MaximumBytes = 524_288;
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal TestDatabase Harness { get; } = new();
    private ZoneTreeStore? coldStore;
    private bool disposed;
    private bool acquisitionPending;
    private bool closeAttempted;
    private bool closeReturned;
    private ExceptionDispatchInfo? originalAcquisitionFailure;
    private ExceptionDispatchInfo? originalCloseFailure;
    private const string AcquisitionNotJoined = "The cold read-view owner acquisition is unknown; retain its root.";
    private const string CloseNotJoined = "The original read-view owner close is unknown; retain its root.";
    internal ZoneTreeStore Store => coldStore is null ? Harness.Store : coldStore;
    internal DatabaseEngine Database => coldStore is null ? Harness.Database : DocumentIndexCommittedScanOperations.CreateEngine(Store);
    internal string[] OldImage { get; set; } = [];
    internal string[] FinalImage { get; set; } = [];
    internal StoreIdentity Identity { get; set; } = null!;
    internal long OldPosition
    {
        get; set;
    }
    internal long FinalPosition
    {
        get; set;
    }
    internal Command<DatabaseCommand> Writer { get; set; } = null!;
    internal CommitReceipt WriterReceipt { get; set; } = null!;
    internal Command<DatabaseCommand> Healthy { get; set; } = null!;
    internal CommitReceipt HealthyReceipt { get; set; } = null!;
    internal byte[] WriterOutcome { get; set; } = [];
    internal byte[] HealthyOutcome { get; set; } = [];
    internal ZoneTreeReadCutLease Capture(CancellationToken token = default)
        => Store.Read(view => Store.CaptureReadCut(view, new(MaximumRows, MaximumBytes, TimeSpan.FromMinutes(1)), token));
    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CloseCurrent();
        var options = new ZoneTreeStoreOptions(Harness.Directory);
        var execution = UnitExecutionOptions.StorageExecution();
        var cache = UnitExecutionOptions.PointCacheExecution();
        acquisitionPending = true;
        try
        {
            coldStore = new(options, execution, cache);
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        acquisitionPending = false;
        closeAttempted = false;
        closeReturned = false;
    }

    public void Dispose()
    {
        disposed = true;
        var failures = new List<Exception>();
        try
        {
            CloseCurrent();
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (failures.Count == 0)
        {
            try
            {
                Harness.Dispose();
            }
            catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CloseCurrent()
    {
        if (acquisitionPending)
        {
            originalAcquisitionFailure?.Throw();
            throw new InvalidOperationException(AcquisitionNotJoined);
        }
        if (closeAttempted)
        {
            originalCloseFailure?.Throw();
            if (!closeReturned)
            {
                throw new InvalidOperationException(CloseNotJoined);
            }
            return;
        }
        closeAttempted = true;
        try
        {
            if (coldStore is null)
            {
                Harness.JoinOwnedResources();
            }
            else
            {
                coldStore.Dispose();
            }
            closeReturned = true;
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
    }
}
