using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextProtectedRebuildColdOwner : IDisposable
{
    private readonly TestDatabaseOriginalResourceCommands originalResources = new();
    internal EntityRef Reference(EntityRef literal) => originalResources.Reference(literal);
    internal TestDatabase Source { get; } = new();
    internal ZoneTreeStore Store
    {
        get; private set;
    }
    internal DatabaseEngine Database
    {
        get; private set;
    }
    private RuntimeTextProjection? projection;
    internal string ProjectionRoot => Path.Combine(Source.Directory, "protected-cold-text");
    internal SearchEngine Search
    {
        get
        {
            projection ??= new(ProjectionRoot,
                Store.Identity.NodeId, UnitTextOptions.Execution());
            return new(Database, UnitExecutionOptions.QueryExecution(), projection);
        }
    }

    internal TextProtectedRebuildColdOwner()
    {
        Store = Source.Store;
        Database = Source.Database;
    }

    internal static async Task RunAsync(Func<TextProtectedRebuildColdOwner, CancellationToken, Task> body,
        CancellationToken token)
    {
        TextProtectedRebuildColdOwner? owner = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            owner = new();
            await body(owner, token);
        }, failures);
        ServerFailureObserver.Observe(() => owner?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Retained Send<T>(OperationKind kind, T request, Guid? id = null) where T : notnull
    {
        var operation = Database.CreateCommand(kind, id ?? Guid.NewGuid(), "root",
            Database.EvaluationClock.GetUtcNow(), request);
        var before = (TestDatabaseOriginalResourceCommands.Begin(Database, operation)).Value;
        var result = Database.ApplyEmbedded(operation, cancellationToken: default);
        originalResources.Observe(Database, operation, result, before);
        return new(operation, result);
    }

    internal void Reopen(bool removeProjection)
    {
        var original = Store.Identity;
        CloseCurrent();
        if (removeProjection)
        {
            Directory.Delete(ProjectionRoot, recursive: true);
        }
        Store = new(new(Source.Directory), Source.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        Database = Source.CreateConfiguredDatabase(Store);
        if (!OrleansSerialization.Serialize(Store.Identity).SequenceEqual(
            OrleansSerialization.Serialize(original)))
        {
            throw new InvalidOperationException("The genuine canonical cold owner identity changed.");
        }
    }

    private void CloseCurrent()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => { projection?.Dispose(); projection = null; }, failures);
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(CloseCurrent, failures);
        ServerFailureObserver.Observe(Source.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal sealed record Retained(Command<DatabaseCommand> Operation, Result<object> Result);
}
