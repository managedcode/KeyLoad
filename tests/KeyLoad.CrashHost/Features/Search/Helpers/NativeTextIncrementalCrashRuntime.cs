using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.Search;

internal sealed class NativeTextIncrementalCrashRuntime : IAsyncDisposable
{
    private const int CanonicalDirectoryIndex = 0;
    private const long MaximumRetainedEntries = 16;
    private readonly ServiceProvider services;
    private readonly NativeTextIncrementalMaintenanceService owner;
    private readonly ReplicaMaterializer materializer;
    private Guid session = Guid.NewGuid();
    internal DatabaseEngine Database => Node.Database;
    internal ServerRuntimeOptions Options => services.GetRequiredService<ServerRuntimeOptions>();
    internal string ProjectionRoot { get; }
    internal string EvidenceRoot { get; }
    internal ReplicaCrashNode Node { get; }

    internal NativeTextIncrementalCrashRuntime(string root, Guid incarnation,
        Action<NativeTextFaultStage>? observer = null)
    {
        EvidenceRoot = root;
        Node = ReplicaCrashNode.OpenTarget(root, incarnation);
        ReplicaMaterializer? openedMaterializer = null;
        ServiceProvider? openedServices = null;
        try
        {
            Node.Snapshots.Recover();
            materializer = openedMaterializer = new(Node.Database, Node.Log, Node.Snapshots, CrashExecutionOptions.Replica());
            var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
            registrations.AddSingleton(CrashExecutionOptions.DatabaseLimits(Node.Database.Limits));
            services = openedServices = registrations.BuildServiceProvider();
            var physicalRoot = Path.GetDirectoryName(ReplicaCrashNode.TargetStoreDirectories(root)[CanonicalDirectoryIndex])
                ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
            ProjectionRoot = Path.Combine(physicalRoot, NativeTextIncrementalProtocol.RootDirectory);
            owner = new(Node.Database, physicalRoot, Node.Canonical.Identity.NodeId,
                services.GetRequiredService<ServerRuntimeOptions>(), Node.Database.EvaluationClock, observer);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (openedMaterializer is not null)
            { ServerFailureObserver.Observe(() => openedMaterializer.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            if (openedServices is not null)
            { ServerFailureObserver.Observe(openedServices.Dispose, failures); }
            try
            { Node.Dispose(); }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<RankedDocument[]> SelectedQueryAsync(TextIndexMaintenanceRequest request, string term)
        => NativeTextIncrementalCrashQuery.ExecuteAsync(Database, owner, Options, ProjectionRoot, request, term);

    internal Task ResumeAppliedAsync(CancellationToken token)
        => materializer.WaitForApplyAsync(Node.Log.State.CommittedIndex, token);

    internal async Task<T> CommitAsync<T>(OperationKind kind, object payload, Guid commandId, CancellationToken token)
    {
        const long InitialTerm = 0;
        const long Increment = 1;
        var operation = Database.NormalizeOperation(new ReplicatedOperation(commandId, kind,
            CrashFixtureValues.Principal, Database.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)));
        if (Node.Log.State.LastIndex > MaximumRetainedEntries)
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        for (var retained = Increment; retained <= Node.Log.State.LastIndex; retained += Increment)
        {
            token.ThrowIfCancellationRequested();
            var original = Node.Log.ReadEntry(retained)
                ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
            if (original.Operation?.Id != commandId)
            { continue; }
            if (retained > Node.Log.State.CommittedIndex)
            { materializer.Commit(retained); }
            await materializer.WaitForApplyAsync(retained, token).ConfigureAwait(false);
            return Database.ResolveOutcome(operation).Get<T>();
        }
        if (Node.Log.State.Term == InitialTerm)
        { Node.Log.SaveTermAndVote(NativeTextIncrementalCrashProtocol.FirstTerm, Node.Configuration.LocalId); }
        var index = checked(Node.Log.State.LastIndex + Increment);
        Node.Log.Append([new ReplicaEntry(index, Node.Log.State.Term, operation)]);
        materializer.Commit(index);
        await materializer.WaitForApplyAsync(index, token).ConfigureAwait(false);
        return Database.ResolveOutcome(operation).Get<T>();
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
        { await owner.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        try
        { await materializer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        try
        { await services.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        try
        { Node.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
