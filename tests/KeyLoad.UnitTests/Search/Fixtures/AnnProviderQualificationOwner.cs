using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnProviderQualificationOwner : IDisposable
{
    internal TestDatabase Source { get; } = new();
    internal ZoneTreeStore Store
    {
        get; private set;
    }
    internal DatabaseEngine Database
    {
        get => field ?? throw new InvalidOperationException(MissingOwner);
        private set;
    }
    private TestDatabaseReplicaAdmission? native;
    internal TestDatabaseReplicaAdmission Native => native ?? throw new InvalidOperationException(MissingOwner);
    internal Guid OriginalJournalNodeId
    {
        get;
    }
    internal string SnapshotPath => Path.Combine(Source.Directory, SnapshotFile);

    internal AnnProviderQualificationOwner()
    {
        Store = Source.Store;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            Database = Source.CreateConfiguredDatabase(Store);
            native = new(Database, Store, Source.Directory, UnitExecutionOptions.ReplicaExecution());
        }, failures);
        if (failures.Count != NoFailures)
        {
            ServerFailureObserver.Observe(Source.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        OriginalJournalNodeId = Native.JournalNodeId;
    }

    internal void Reopen()
    {
        AnnProviderQualificationCold.RequireOriginalRoot(Source.Directory);
        AnnProviderQualificationCold.RequireOriginalRoot(Path.Combine(Source.Directory, JournalDirectory));
        var identity = Store.Identity;
        CloseCurrent();
        AnnProviderQualificationCold.RequireOriginalRoot(Source.Directory);
        AnnProviderQualificationCold.RequireOriginalRoot(Path.Combine(Source.Directory, JournalDirectory));
        Store = new(new(Source.Directory)
        {
            Incarnation = identity.Incarnation,
            SigningKey = identity.SigningKey
        }, Source.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        Database = Source.CreateConfiguredDatabase(Store);
        native = new(Database, Store, Source.Directory, UnitExecutionOptions.ReplicaExecution(),
            originalNodeId: OriginalJournalNodeId);
    }

    private void CloseCurrent()
    {
        var failures = new List<Exception>();
        try
        {
            native?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        native = null;
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        {
            CloseCurrent();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.Observe(Source.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
