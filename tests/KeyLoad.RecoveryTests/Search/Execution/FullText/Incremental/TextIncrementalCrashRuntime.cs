using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.RecoveryTests.Search;

/// <summary>Owns real canonical storage and execution text files across abrupt process recovery.</summary>
internal sealed class TextIncrementalCrashRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly TextIncrementalMaintenanceService owner;
    private Guid session = Guid.NewGuid();
    internal ZoneTreeStore Store { get; }
    internal DatabaseEngine Database { get; }
    internal ServerRuntimeOptions Options => services.GetRequiredService<ServerRuntimeOptions>();
    internal string PhysicalRoot { get; }
    internal string ProjectionRoot { get; }
    internal string EvidenceRoot { get; }

    internal TextIncrementalCrashRuntime(string root, Guid incarnation,
        Action<TextFaultStage>? observer = null)
    {
        EvidenceRoot = root;
        PhysicalRoot = root;
        Store = new(new(Path.Combine(root, PartitionStoreProtocol.CanonicalDirectory))
        {
            Incarnation = incarnation
        }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        ServiceProvider? openedServices = null;
        try
        {
            Database = CrashDatabase.Create(Store);
            var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
            registrations.AddSingleton(CrashExecutionOptions.DatabaseLimits(Database.Limits));
            services = openedServices = registrations.BuildServiceProvider();
            ProjectionRoot = Path.Combine(PhysicalRoot, TextIncrementalProtocol.RootDirectory);
            owner = new(Database, PhysicalRoot, Store.Identity.NodeId, Options, Database.EvaluationClock, observer);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (openedServices is not null)
            {
                ServerFailureObserver.Observe(openedServices.Dispose, failures);
            }
            ServerFailureObserver.Observe(Store.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<RankedDocument[]> SelectedQueryAsync(TextIndexMaintenanceRequest request, string term)
        => TextIncrementalCrashQuery.ExecuteAsync(Database, owner, Options, ProjectionRoot, request, term);

    internal Task ResumeAppliedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = Database.LastApplied;
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal Task<T> CommitAsync<T>(OperationKind kind, object payload, Guid commandId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var operation = Database.CreateCommand(kind, commandId, CrashFixtureValues.Principal, default, System.Text.Json.JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options));
        return Task.FromResult(Database.ApplyEmbedded(operation, token).Get<T>());
    }

    internal Task<Result<object>> CommitIssuedAsync(Command<DatabaseCommand> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (operation.Value!.Kind != OperationKind.OnlineTextPublicationPhase || operation.CommandId == Guid.Empty)
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        return Task.FromResult(Database.ApplyEmbedded(Database.OwnCommand(operation), token));
    }

    internal Task<TextMaintenanceCapabilityResult> PhaseAsync(TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityKind phase, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, ProjectionBatchResult? acknowledged = null,
        CancellationToken token = default)
    {
        var principal = Database.Store.Read(view => Database.Principal(view, CrashFixtureValues.Principal,
            Database.EvaluationClock.GetUtcNow()));
        return owner.ExecuteAsync(principal, new(session, request, phase, page, intent, acknowledged), token);
    }

    internal async Task RetireSessionAsync()
    {
        await owner.AbortAsync(session).ConfigureAwait(false);
        session = Guid.NewGuid();
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        try
        {
            await services.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
