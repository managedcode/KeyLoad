using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Search;

internal sealed class AnnMaintenanceService(string directory, DatabaseEngine database,
    ServerRuntimeOptions configured, TimeProvider clock) : IAnnMaintenance, IAnnProjection, IAsyncDisposable
{
    private readonly AnnMaintenanceAdmissions admitted = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly AnnMaintenanceState state = new(() => AnnOwnership.Create(directory,
        database.Store.Identity, configured.RuntimeAnn, configured.Core.PackedAnn, configured.Core.PackedAnnStorage));

    public async Task<AnnProjectionEnrollment?> ReadEnrollmentAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, CancellationToken cancellationToken)
        => (await AnnEnrollmentRead.ReadAsync(database, principal, initialRequest, configured,
            clock, admitted, state.EnsureOwner, cancellationToken)).Value;

    public async Task<AnnProjectionEnrollment?> ReadEnrollmentBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return (await AnnEnrollmentRead.ReadAsync(database, principal, initialRequest, configured,
            clock, admitted, state.EnsureOwner, token, work, binding)).Value;
    }

    public Task<AnnProjectionPublication> ReadPublishedBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return AnnProjectionPublicationRead.ReadAsync(database, principal, request, configured,
            clock, admitted, state.EnsureOwner, token, work, binding);
    }

    public async Task<AnnMaintenanceResult?> AdmitOriginalAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ProjectionConsumerInfo originalConfigure, CancellationToken cancellationToken)
        => (await AnnOriginalOperationExecution.ExecuteAsync(database, principal, request, originalConfigure,
            null, configured, clock, admitted, state.EnsureOwner, cancellationToken)).Result;

    public async Task SettleOriginalResultAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult originalResult, CancellationToken cancellationToken)
        => _ = await AnnOriginalOperationExecution.ExecuteAsync(database, principal, request, null,
            originalResult, configured, clock, admitted, state.EnsureOwner, cancellationToken,
            state.RequireOriginal(request));

    internal Task<(AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent)> ExecuteOriginalCapabilityAsync(
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, CancellationToken token, ReadExecutionBudget? originalBudget = null)
        => AnnOriginalOperationExecution.ExecuteAsync(database, principal, request.Maintenance,
            request.OriginalConfigure, request.FullOriginalResult, configured, clock, admitted, state.EnsureOwner, token,
            request.Kind == AnnMaintenanceCapabilityKind.SettleOriginalResult ? state.RequireOriginal(request.Maintenance) : null, originalBudget);

    internal void RequireOriginalCapabilitySession(PrincipalRecord principal, AnnMaintenanceCapabilityRequest request)
        => state.RequireSession(principal.Id, request);

    public Task<AnnProjectionPublication> ReadPublishedAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, CancellationToken cancellationToken)
        => AnnProjectionPublicationRead.ReadAsync(database, principal, request, configured,
            clock, admitted, state.EnsureOwner, cancellationToken);

    public IAnnProjectionLease Acquire(IKeyValueView view, AnnProjectionSelection request, ReadExecutionBudget budget)
    {
        using var originalOperation = admitted.Enter();
        budget.Check();
        return AnnPublicAcquisition.Acquire(database, view, state.RequirePublicOwner(), request, configured, budget);
    }

    public Task<AnnMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken token)
        => ExecuteOwnedAsync(principal, request, null, null, token);

    public Task<AnnMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, DateTimeOffset requestExpiry,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteOwnedAsync(principal, request, requestExpiry, binding, token);
    }

    private async Task<AnnMaintenanceCapabilityResult> ExecuteOwnedAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, DateTimeOffset? expiry,
        IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        AnnOriginalCapabilityExecution.Require(request);
        if (request.Kind is not (AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult))
        {
            AnnMaintenanceAdmission.Request(request.Maintenance, request.SessionId, database.Store.Identity.NodeId);
        }
        if (request.Kind == AnnMaintenanceCapabilityKind.Release)
        {
            using var release = new MaintenanceSessionWork(clock,
                expiry, shutdown.Token);
            using var releaseStage = release.EnterStage(binding, token);
            var released = await ExecuteCoreAsync(principal, request, release, token);
            release.Budget.CheckResult(released);
            return released;
        }
        var work = admitted.RequireWork(principal.Id, request, clock, expiry, shutdown.Token);
        using var stage = work.EnterStage(binding, token);
        var result = await ExecuteCoreAsync(principal, request, work, token);
        work.Budget.CheckResult(result);
        return result;
    }

    private async Task<AnnMaintenanceCapabilityResult> ExecuteCoreAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, MaintenanceSessionWork work, CancellationToken token)
    {
        AnnOriginalCapabilityExecution.Require(request);
        if (request.Kind is AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult)
        {
            return await AnnOriginalCapabilityExecution.ExecuteAsync(this, principal, request, token, work.Budget);
        }
        using var originalOperation = admitted.Enter();
        token.ThrowIfCancellationRequested();
        if (!principal.ClusterAdministrator || principal.Revoked)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, AnnProtocol.InvalidSource);
        }
        AnnMaintenanceAdmission.Request(request.Maintenance, request.SessionId, database.Store.Identity.NodeId);
        if (request.Kind == AnnMaintenanceCapabilityKind.Release)
        {
            return await AnnMaintenanceLifecycle.ReleaseAsync(database, principal, request, directory,
                state.GetOwner, state.EnsureOwner, token);
        }
        if (request.Kind == AnnMaintenanceCapabilityKind.Begin)
        {
            return await state.BeginAsync(database, principal, request, configured, work, shutdown.Token);
        }
        var stage = state.RequireStage(principal.Id, request);
        return await stage.Owner.RunAsync(stages => AnnMaintenanceStage.Execute(database, stage.Owner,
            stages, stage.Session, principal, request, configured, token));
    }

    internal AnnMaintenanceWork? ObserveWork(Guid id) => (state.ObserveWork(id)).Value;
    public async Task AbortAsync(Guid id)
    {
        if ((admitted.FindWork(id)).Value is { } work)
        {
            await work.CancelAndJoinStagesAsync();
            await state.AbortAsync(id);
            admitted.RetireWork(id);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await state.DisposeAsync(CloseAdmissionsAsync);
        admitted.RetireWork();
        shutdown.Dispose();
    }

    private async Task CloseAdmissionsAsync()
    {
        var joined = admitted.CloseAndJoinAsync();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(admitted.JoinStagesAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => joined, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
