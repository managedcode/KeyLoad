using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Owns one real ZoneTree-backed database, its bootstrap principal and its private directory.</summary>
internal sealed class EmbeddedBenchmarkDatabase(EmbeddedBenchmarkRuntimeOptions options, TimeProvider timeProvider) : IDisposable
{
    internal const string InactiveFixtureMessage = "The benchmark fixture is not initialized.";
    internal const string PrincipalId = "root";
    internal const string TenantId = "benchmark";
    internal const string DatabaseId = "database";
    private const string TemporaryDirectoryPrefix = "keyload-benchmark-";
    private const string PrincipalScope = "system";
    private const string Wildcard = "*";
    private const string BenchmarkCredential = "root.benchmark-credential-32-characters";
    private const string GuidFormat = "N";
    private readonly string directory = Path.Combine(AppContext.BaseDirectory,
        TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private ZoneTreeStore? store;
    private DatabaseEngine? database;
    private bool disposed;

    internal DatabaseEngine Database
        => database ?? throw new InvalidOperationException(InactiveFixtureMessage);

    /// <summary>Opens the store, bootstraps the administrator and configures one document collection.</summary>
    internal void Initialize(string collection)
    {
        store = new(new(directory), options.Storage, options.PointCache, timeProvider);
        database = new(store, new AuthorizationPolicy(), options.Database, options.DueWork, options.EventSource,
            options.Messaging, options.GraphExecution, options.ChangeFeedExecution, options.RuntimeClaimsExecution,
            options.TimeSeriesExecution, timeProvider);
        database.Bootstrap(new(PrincipalId, PrincipalScope,
            [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        {
            ClusterAdministrator = true
        },
            AuthorizationExecution.Credential(PrincipalId, PrincipalId, BenchmarkCredential));
        Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(TenantId, DatabaseId, new(collection, ResourceKind.Collection, collection)));
    }

    /// <summary>Commits one command through the real embedded engine path and durable store.</summary>
    /// <remarks>The embedded path selects the command time inside the commit gate, so concurrent callers commit
    /// in the order they enter it.</remarks>
    internal void Commit(PartitionRef partition, params Mutation[] mutations)
    {
        var commandId = Guid.NewGuid();
        Submit(OperationKind.Batch, new CommandRequest(commandId, partition, [.. mutations]), commandId);
    }

    /// <summary>Commits one command per document as consecutive canonical commands sharing storage commits.</summary>
    /// <remarks>This is the path the database grain uses for commands that arrive together: one preparation and one
    /// atomic commit with one journal flush for the whole group.</remarks>
    internal void CommitGroup(PartitionRef partition, IReadOnlyList<Mutation> documents)
    {
        var pending = documents.Select(document => CreateCommand(OperationKind.Batch, Guid.NewGuid(),
            id => new CommandRequest(id, partition, [document]))).ToArray();
        while (pending.Length > 0)
        {
            var prepared = Database.PrepareStorageOperations(pending, CancellationToken.None);
            var batch = Database.ApplyStorageOperations(prepared, Database.LastApplied + 1, CancellationToken.None);
            foreach (var applied in batch.Applied)
            {
                Require(applied.Result);
            }
            pending = pending[batch.Applied.Length..];
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            store?.Dispose();
        }
        finally
        {
            store = null;
            database = null;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private void Submit<T>(OperationKind kind, T payload, Guid? commandId = null)
        => Require(Database.ApplyEmbedded(CreateCommand(kind, commandId ?? Guid.NewGuid(), _ => payload), CancellationToken.None));

    private Command<DatabaseCommand> CreateCommand<T>(OperationKind kind, Guid commandId, Func<Guid, T> payload)
        => Database.CreateCommand(kind, commandId, PrincipalId, timeProvider.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(payload(commandId), JsonDefaults.Options));

    private static void Require(Result<object> result)
    {
        if (result.IsFailed)
        {
            throw Errors.Fail(Errors.Code(result.Problem)!.Value, result.Problem.Detail!);
        }
    }
}
