using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProtectedRebuildColdOwner : IDisposable
{
    internal TestDatabase Source { get; } = new();
    internal ZoneTreeStore Store
    {
        get; private set;
    }
    internal DatabaseEngine Database
    {
        get; private set;
    }
    private NativeTextProjection? projection;
    internal string ProjectionRoot => Path.Combine(Source.Directory, "protected-cold-text");
    internal SearchEngine Search
    {
        get
        {
            projection ??= new(ProjectionRoot, UnitExecutionOptions.DatabaseLimits(Database.Limits),
                Store.Identity.NodeId, UnitNativeTextOptions.Execution());
            return new(Database, UnitExecutionOptions.QueryExecution(), projection);
        }
    }

    internal NativeTextProtectedRebuildColdOwner()
    {
        Store = Source.Store;
        Database = Source.Database;
    }

    internal static async Task RunAsync(Func<NativeTextProtectedRebuildColdOwner, CancellationToken, Task> body,
        CancellationToken token)
    {
        NativeTextProtectedRebuildColdOwner? owner = null;
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

    internal Retained Send<T>(OperationKind kind, T request, Guid? id = null)
    {
        var operation = Database.CreateCommand(kind, id ?? Guid.NewGuid(), "root",
            Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        return new(operation, Database.ApplyEmbedded(operation, cancellationToken: default));
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
        if (!NativeSerialization.Serialize(Store.Identity).SequenceEqual(
            NativeSerialization.Serialize(original)))
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
