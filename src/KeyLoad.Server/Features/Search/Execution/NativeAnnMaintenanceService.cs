using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceService(string directory, DatabaseEngine database,
    ServerRuntimeOptions configured, TimeProvider clock) : INativeAnnMaintenance, IAnnProjection, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly NativeAnnMaintenanceAdmissions admitted = new();
    private NativeAnnGenerationOwner? owner;
    private NativeAnnMaintenanceSession? session;
    private bool beginning;
    private bool closed;
    private Task? disposal;

    public Task<AnnProjectionEnrollment?> ReadEnrollmentAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, CancellationToken cancellationToken)
        => NativeAnnEnrollmentRead.ReadAsync(database, principal, initialRequest, configured,
            clock, admitted, EnsureOwner, cancellationToken);

    public async Task<AnnMaintenanceResult?> AdmitOriginalAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ProjectionConsumerInfo originalConfigure, CancellationToken cancellationToken)
        => (await NativeAnnOriginalOperationExecution.ExecuteAsync(database, principal, request, originalConfigure,
            null, configured, clock, admitted, EnsureOwner, cancellationToken).ConfigureAwait(false)).Result;

    public async Task SettleOriginalResultAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult originalResult, CancellationToken cancellationToken)
        => _ = await NativeAnnOriginalOperationExecution.ExecuteAsync(database, principal, request, null,
            originalResult, configured, clock, admitted, EnsureOwner, cancellationToken,
            RequireOriginalSession(request)).ConfigureAwait(false);

    internal Task<(AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent)> ExecuteOriginalCapabilityAsync(
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, CancellationToken token)
        => NativeAnnOriginalOperationExecution.ExecuteAsync(database, principal, request.Maintenance,
            request.OriginalConfigure, request.FullOriginalResult, configured, clock, admitted, EnsureOwner, token,
            request.Kind == AnnMaintenanceCapabilityKind.SettleOriginalResult ? RequireOriginalSession(request.Maintenance) : null);

    internal void RequireOriginalCapabilitySession(PrincipalRecord principal, AnnMaintenanceCapabilityRequest request)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            (session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership))
                .Require(request.SessionId, request.Maintenance, principal.Id);
        }
    }

    private NativeAnnMaintenanceSession RequireOriginalSession(AnnMaintenanceRequest request)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var current = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            if (!NativeAnnOriginalResults.SameRequest(current.Request, request))
            {
                throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.InvalidSource);
            }
            return current;
        }
    }

    public async Task<AnnMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken cancellationToken)
    {
        NativeAnnOriginalCapabilityExecution.Require(request);
        if (request.Kind is AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult)
        {
            return await NativeAnnOriginalCapabilityExecution.ExecuteAsync(this, principal, request, cancellationToken).ConfigureAwait(false);
        }
        using var originalOperation = admitted.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
        cancellationToken.ThrowIfCancellationRequested();
        if (!principal.ClusterAdministrator || principal.Revoked)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, NativeAnnProtocol.InvalidSource);
        }
        NativeAnnMaintenanceAdmission.Request(request.Maintenance, request.SessionId, database.Store.Identity.NodeId);
        if (request.Kind == AnnMaintenanceCapabilityKind.Release)
        {
            return await ReleaseAsync(principal, request, cancellationToken).ConfigureAwait(false);
        }
        if (request.Kind == AnnMaintenanceCapabilityKind.Begin)
        {
            return await BeginAsync(principal, request, cancellationToken).ConfigureAwait(false);
        }
        NativeAnnGenerationOwner currentOwner;
        NativeAnnMaintenanceSession currentSession;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            currentOwner = owner ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            currentSession = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            currentSession.Require(request.SessionId, request.Maintenance, principal.Id);
        }
        return await currentOwner.RunAsync(stages => NativeAnnMaintenanceStage.Execute(database, currentOwner,
            stages, currentSession, principal, request, configured, clock, cancellationToken)).ConfigureAwait(false);
    }

    public Task<AnnProjectionPublication> ReadPublishedAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, CancellationToken cancellationToken)
        => NativeAnnProjectionPublicationRead.ReadAsync(database, principal, request, configured,
            clock, admitted, EnsureOwner, cancellationToken);

    public IAnnProjectionLease Acquire(IKeyValueView view, AnnProjectionSelection request,
        ReadExecutionBudget budget)
    {
        using var originalOperation = admitted.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
        budget.Check();
        NativeAnnGenerationOwner actual;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            actual = owner ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        }
        return NativeAnnPublicAcquisition.Acquire(database, view, actual, request, configured, budget);
    }

    private Task<AnnMaintenanceCapabilityResult> ReleaseAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken token)
        => NativeAnnMaintenanceLifecycle.ReleaseAsync(database, principal, request, directory,
            GetOwner, EnsureOwner, token);

    private NativeAnnGenerationOwner? GetOwner()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return owner;
        }
    }

    private async Task<AnnMaintenanceCapabilityResult> BeginAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (beginning || session is not null)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound);
            }
            beginning = true;
        }
        try
        {
            return await NativeAnnMaintenanceBeginning.RunAsync(database, principal, request, configured,
                clock, gate, () => owner, EnsureOwner, created => { lock (gate) { session = created; } }, token)
                .ConfigureAwait(false);
        }
        finally { lock (gate) { beginning = false; } }
    }

    private NativeAnnGenerationOwner EnsureOwner()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return owner ??= NativeAnnOwnership.Create(directory, database.Store.Identity,
                configured.NativeAnn, configured.Core.PackedAnn, configured.Core.PackedAnnStorage,
                configured.GrainRouting.Value.MaximumRequestProducers);
        }
    }

    internal NativeAnnMaintenanceWork? ObserveWork(Guid id)
    {
        lock (gate)
        {
            if (closed || session?.Id != id || session.Replay is null)
            {
                return null;
            }
            var observed = session.Observation;
            return new(id, session.Replay.WorkUnits, observed?.Kind, observed?.Budget.WrittenBytes ?? NativeAnnMaintenanceWork.NoWrittenBytes);
        }
    }

    public async Task AbortAsync(Guid id)
    {
        NativeAnnGenerationOwner? actual;
        lock (gate)
        {
            actual = owner;
        }
        if (actual is null)
        {
            return;
        }
        await actual.RunAsync(_ =>
        {
            lock (gate)
            {
                if (session?.Id == id)
                {
                    session.Memory.Dispose();
                    session = null;
                }
            }
            return true;
        }).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
            disposal ??= NativeAnnMaintenanceLifecycle.DisposeAsync(admitted.CloseAndJoinAsync(),
                GetDisposingOwner, () => { lock (gate) { session = null; } });
            return new(disposal);
        }
    }

    private NativeAnnGenerationOwner? GetDisposingOwner()
    {
        lock (gate)
        {
            return owner;
        }
    }
}
