using KeyLoad.Clustering;
using KeyLoad.Clustering.Search;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineMaintenanceService : IOnlineTextMaintenance, IAsyncDisposable
{
    private readonly DatabaseEngine database;
    private readonly ServerRuntimeOptions options;
    private readonly TimeProvider clock;
    private readonly TextResourceOwnership resources;
    private readonly TextOnlineCatalogOwner catalog;
    private readonly TextOnlineOriginalReplay replay;
    private readonly TextOnlineRoot root;
    private readonly TextOnlineSessions sessions;
    private readonly TextOnlineWorker worker = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal TextOnlineMaintenanceService(DatabaseEngine database, string directory, Guid nodeId,
        ServerRuntimeOptions options, TimeProvider clock, TextResourceOwnership resources,
        Action<TextFaultStage>? observer = null)
    {
        this.database = database;
        this.options = options;
        this.clock = clock;
        this.resources = resources;
        sessions = new(options.RuntimeText.Value.MaximumActiveLeases);
        try
        {
            root = new(directory, nodeId, options.RuntimeText, resources);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(shutdown.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catalog = new(database, root, options, clock, resources, observer);
        replay = new(database, options, clock, resources, sessions, catalog, CleanupSessionAsync);
    }

    public Task<OnlineTextCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset originalExpiry, CancellationToken token)
        => worker.RunAsync(() => ExecuteOwnedAsync(principal, request, originalExpiry, null, token));

    public Task<OnlineTextCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset originalExpiry,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return worker.RunAsync(() => ExecuteOwnedAsync(principal, request, originalExpiry, binding, token));
    }

    private async Task<OnlineTextCapabilityResult> ExecuteOwnedAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry,
        IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await catalog.ObserveCompletedRetirementsAsync();
        if (!principal.ClusterAdministrator || principal.Revoked || request.SessionId == Guid.Empty
            || request.Request.NodeId != database.Store.Identity.NodeId)
        {
            throw TextErrors.Ownership();
        }
        var session = request.Kind == OnlineTextCapabilityKind.ResolveOriginal ? null : sessions.Require(request.SessionId);
        MaintenanceSessionWork? created = null;
        try
        {
            var work = session?.Work;
            if (work is null)
            {
                created = new MaintenanceSessionWork(options.Core.DatabaseLimits, clock, expiry, shutdown.Token);
                work = created;
                sessions.BeginWork(request.SessionId, created);
            }
            using var stage = work.EnterStage(binding, token);
            work.RequireExpiry(expiry);
            if (request.Kind == OnlineTextCapabilityKind.ResolveOriginal)
            {
                var resolved = await ResolveCommittedOriginalAsync(principal, request, expiry, work.Budget, token);
                if (resolved is not null)
                {
                    return resolved;
                }
                session = await sessions.AddOwnedAsync(new(request.SessionId, request.Request, principal.Id, expiry, work));
                created = null;
            }
            return await ExecuteStageAsync(session ?? throw TextErrors.Ownership(), principal, request, expiry);
        }
        finally
        {
            if (created is not null)
            {
                created.Dispose();
                sessions.RemoveWorkWithoutSession(request.SessionId, created);
            }
        }
    }

    private async Task<OnlineTextCapabilityResult> ExecuteStageAsync(TextOnlineSession session, PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry)
    {
        session.Require(request.SessionId, request.Request, principal.Id, expiry);
        if (request.Kind == OnlineTextCapabilityKind.SettleCheckpoint)
        {
            await TextOnlineCheckpointSettlement.ExecuteAsync(database, session,
                request.Acknowledged ?? throw TextErrors.Corrupt(), resources, options);
        }
        else if (request.Kind == OnlineTextCapabilityKind.ReconcileCommitted)
        {
            await catalog.ReconcileCommittedAsync(session);
        }
        else
        {
            lock (session.Gate)
            {
                return TextOnlineStageExecutor.Execute(database, root, resources, options, clock,
                    RequireCurrentAuthority, principal, session, request);
            }
        }
        return TextOnlineCapabilityResult.Create(session);
    }

    private void RequireCurrentAuthority(TextOnlineSession session)
    {
        var fresh = TextOnlineCapture.CurrentMetadata(database, session);
        TextOnlineCapture.RequireSameAuthority(session, fresh);
        session.Current = fresh;
    }

    public OnlineTextPublicationWork RequirePublicationWork(Guid sessionId,
        OnlineTextIndexMaintenanceRequest request, string principalId)
    {
        var session = sessions.Require(sessionId);
        lock (session.Gate)
        {
            session.Require(sessionId, request, principalId, session.OriginalExpiry);
            return session.RequirePreparedPublication();
        }
    }

    public OnlineTextPublicationWork? TryRequireCheckpointWork(CommitProjectionBatchRequest request,
        string principalId, DateTimeOffset originalExpiry)
        => sessions.TryMatchCheckpoint(request, principalId, originalExpiry);

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposal ??= DisposeCoreAsync();
            return new(disposal);
        }
    }

    internal Task[] CaptureOriginalRetirements() => catalog.CaptureOriginalRetirements();

    internal TextSelectedProjectionLease? AcquireCurrent(KeyLoad.Storage.IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => catalog.AcquireCurrent(view, principal, resource, request, budget);
}
