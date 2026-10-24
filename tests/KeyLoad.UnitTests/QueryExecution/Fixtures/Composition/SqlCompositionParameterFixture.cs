using System.Runtime.ExceptionServices;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.DatabaseComposition;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlCompositionParameterFixture : IDisposable
{
    private const string Unknown = "The original composed SQL fixture execution owner has not released.";
    private readonly List<StorageOwnerSettlement> attempts = [];
    private ZoneTreeStore? cold;
    private bool sourceJoined;
    private bool closeAttempted;
    private bool disposed;
    private bool preserve;
    private ExceptionDispatchInfo? closeFailure;
    private ExceptionDispatchInfo? disposalFailure;
    internal TestDatabase Source { get; } = new();
    internal ZoneTreeStore Store => cold ?? (!sourceJoined ? Source.Store : throw new InvalidOperationException(Unknown));
    private DatabaseEngine? ColdDatabase
    {
        get; set;
    }
    internal DatabaseEngine Database => ColdDatabase ?? (!sourceJoined ? Source.Database : throw new InvalidOperationException(Unknown));
    internal QueueGraphIdentityState Seed { get; set; } = null!;
    internal List<SqlCompositionParameterOriginal> Originals { get; } = [];
    internal void RetainEvidence() => preserve = true;

    internal void Reopen()
    {
        CloseCold();
        Source.JoinOwnedResources();
        sourceJoined = true;
        if (attempts.Any(attempt => !attempt.IsReleased))
        {
            throw new InvalidOperationException(Unknown);
        }
        var attempt = new StorageOwnerSettlement();
        attempts.Add(attempt);
        cold = ZoneTreeStore.OpenOwned(new(Source.Directory)
        {
            Incarnation = Source.Store.Identity.Incarnation
        }, Source.StorageExecution, UnitExecutionOptions.PointCacheExecution(), attempt);
        closeAttempted = false;
        ColdDatabase = Source.CreateConfiguredDatabase(cold);
    }

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
        ServerFailureObserver.Observe(Source.JoinOwnedResources, failures);
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
