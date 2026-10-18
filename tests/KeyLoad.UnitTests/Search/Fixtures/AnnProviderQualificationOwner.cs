using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Uses the actual canonical store and reopens its original identity for provider qualification.</summary>
internal sealed class AnnProviderQualificationOwner : IDisposable
{
    internal TestDatabase Source { get; } = new();
    internal ZoneTreeStore Store { get; private set; }
    internal DatabaseEngine Database { get; private set; }
    internal string SnapshotPath => Path.Combine(Source.Directory, SnapshotFile);
    private bool originalJoined;

    internal AnnProviderQualificationOwner()
    {
        Store = Source.Store;
        Database = Source.Database;
    }

    internal Result<object> Submit(Command<DatabaseCommand> command, bool explicitTime, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return explicitTime ? Database.Apply(command) : Database.ApplyEmbedded(command, token);
    }

    internal StoredOutcome ReadStoredOutcome(Guid commandId)
        => OutcomeStoreOracle.ReadStored(Database, commandId, TestContext.Current!.Execution.CancellationToken);

    internal StoredOutcome ReadStoredOutcome(Command<DatabaseCommand> command)
        => OutcomeStoreOracle.ReadStored(Store, command) ?? throw new InvalidOperationException(MissingOriginal);

    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => Store.Read(_ => (Database.LastApplied, Store.Position));

    internal void Reopen()
    {
        AnnProviderQualificationCold.RequireOriginalRoot(Source.Directory);
        var identity = Store.Identity;
        CloseCurrent();
        AnnProviderQualificationCold.RequireOriginalRoot(Source.Directory);
        Store = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = identity.Incarnation
        }, identity.NodeId, Source.StorageExecution);
        Database = Source.CreateConfiguredDatabase(Store);
    }

    private void CloseCurrent()
    {
        if (!originalJoined)
        {
            originalJoined = true;
            Source.JoinOwnedResources();
        }
        else
        {
            Store.Dispose();
        }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(CloseCurrent, failures);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(Source.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
