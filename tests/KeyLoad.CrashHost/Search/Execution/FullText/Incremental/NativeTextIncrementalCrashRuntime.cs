using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.Search;

/// <summary>Owns real canonical storage and native text files across abrupt process recovery.</summary>
internal sealed class NativeTextIncrementalCrashRuntime : IAsyncDisposable
{
    private const string CanonicalDirectory = "canonical";
    private readonly ServiceProvider services;
    private readonly NativeTextIncrementalMaintenanceService owner;
    private Guid session = Guid.NewGuid();
    internal ZoneTreeStore Store { get; }
    internal DatabaseEngine Database { get; }
    internal ServerRuntimeOptions Options => services.GetRequiredService<ServerRuntimeOptions>();
    internal string PhysicalRoot { get; }
    internal string ProjectionRoot { get; }
    internal string EvidenceRoot { get; }

    internal NativeTextIncrementalCrashRuntime(string root, Guid incarnation,
        Action<NativeTextFaultStage>? observer = null)
    {
        EvidenceRoot = root;
        PhysicalRoot = root;
        Store = new(new(Path.Combine(root, CanonicalDirectory))
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
            ProjectionRoot = Path.Combine(PhysicalRoot, NativeTextIncrementalProtocol.RootDirectory);
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
        => NativeTextIncrementalCrashQuery.ExecuteAsync(Database, owner, Options, ProjectionRoot, request, term);

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
        var operation = new ReplicatedOperation(commandId, kind, CrashFixtureValues.Principal, default,
            System.Text.Json.JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options));
        return Task.FromResult(Database.ApplyEmbedded(operation, token).Get<T>());
    }

    internal Task<OperationResult> CommitIssuedAsync(ReplicatedOperation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (operation.Kind != OperationKind.OnlineTextPublicationPhase || operation.Id == Guid.Empty)
        {
            throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        }
        return Task.FromResult(Database.ApplyEmbedded(Database.VerifyOperationAuthority(operation), token));
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
        await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => services.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
