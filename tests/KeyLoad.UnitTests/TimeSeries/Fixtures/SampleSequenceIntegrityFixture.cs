using System.Runtime.ExceptionServices;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleSequenceIntegrityFixture : IDisposable
{
    private const string Unknown = "The original execution sequence fixture owner has not released.";
    private readonly List<StorageOwnerSettlement> attempts = [];
    private ZoneTreeStore? cold;
    private bool sourceJoined;
    private bool closeAttempted;
    private bool disposed;
    private bool preserve;
    private ExceptionDispatchInfo? closeFailure;
    private ExceptionDispatchInfo? disposalFailure;
    internal SampleChunkCanonicalFixture Source { get; } = new();
    internal ZoneTreeStore Store => cold ?? (!sourceJoined ? Source.Owner.Store : throw new InvalidOperationException(Unknown));
    private DatabaseEngine? ColdDatabase
    {
        get; set;
    }
    internal DatabaseEngine Database => ColdDatabase ?? (!sourceJoined ? Source.Owner.Database : throw new InvalidOperationException(Unknown));
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result, CommandRequest Command, CommitReceipt Expected, byte[] OutcomeBytes)> Originals { get; } = [];
    internal byte[] Counter => KeySpace.Partition("sample-sequence", Source.Owner.Partition,
        SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series);
    internal void RetainEvidence() => preserve = true;
    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), Store.Position));

    internal void Reopen()
    {
        CloseCold();
        Source.Owner.JoinOwnedResources();
        sourceJoined = true;
        if (attempts.Any(attempt => !attempt.IsReleased))
        {
            throw new InvalidOperationException(Unknown);
        }
        var attempt = new StorageOwnerSettlement();
        attempts.Add(attempt);
        cold = ZoneTreeStore.OpenOwned(new(Source.Owner.Directory)
        {
            Incarnation = Source.Owner.Store.Identity.Incarnation
        }, Source.Owner.StorageExecution, UnitExecutionOptions.PointCacheExecution(), attempt);
        closeAttempted = false;
        ColdDatabase = Source.Owner.CreateConfiguredDatabase(cold);
    }

    internal void PutCounter(byte[] bytes)
        => Store.Commit((transaction, _) =>
        {
            transaction.Put(Counter, bytes);
            return true;
        });

    private void CloseCold()
    {
        closeFailure?.Throw();
        if (cold is null)
        {
            return;
        }
        if (closeAttempted)
        {
            throw new InvalidOperationException(Unknown);
        }
        closeAttempted = true;
        try
        {
            cold.Dispose();
            cold = null;
            ColdDatabase = null;
        }
        catch (Exception failure)
        {
            closeFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            disposalFailure?.Throw();
            return;
        }
        disposed = true;
        try
        {
            DisposeCore();
        }
        catch (Exception failure)
        {
            disposalFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
    }

    private void DisposeCore()
    {
        var failures = new List<Exception>();
        try
        {
            CloseCold();
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.Observe(Source.Owner.JoinOwnedResources, failures);
        if (attempts.Any(attempt => !attempt.IsReleased))
        {
            failures.Add(new InvalidOperationException(Unknown));
        }
        if (!preserve && failures.Count == 0)
        {
            try
            {
                Source.Dispose();
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
}
