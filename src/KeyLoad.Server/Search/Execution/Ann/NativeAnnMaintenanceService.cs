using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceService(string directory, DatabaseEngine database,
    ServerRuntimeOptions configured, TimeProvider clock) : INativeAnnMaintenance, IAnnProjection, IAsyncDisposable
{
    private readonly NativeAnnMaintenanceAdmissions admitted = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly NativeAnnMaintenanceState state = new(() => NativeAnnOwnership.Create(directory,
        database.Store.Identity, configured.NativeAnn, configured.Core.PackedAnn, configured.Core.PackedAnnStorage,
        configured.GrainRouting.Value.MaximumRequestProducers));

    public Task<AnnProjectionEnrollment?> ReadEnrollmentAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, CancellationToken cancellationToken)
        => NativeAnnEnrollmentRead.ReadAsync(database, principal, initialRequest, configured,
            clock, admitted, state.EnsureOwner, cancellationToken);

    public Task<AnnProjectionEnrollment?> ReadEnrollmentBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return NativeAnnEnrollmentRead.ReadAsync(database, principal, initialRequest, configured,
            clock, admitted, state.EnsureOwner, token, work, binding);
    }

    public Task<AnnProjectionPublication> ReadPublishedBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return NativeAnnProjectionPublicationRead.ReadAsync(database, principal, request, configured,
            clock, admitted, state.EnsureOwner, token, work, binding);
    }

    public async Task<AnnMaintenanceResult?> AdmitOriginalAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ProjectionConsumerInfo originalConfigure, CancellationToken cancellationToken)
        => (await NativeAnnOriginalOperationExecution.ExecuteAsync(database, principal, request, originalConfigure,
            null, configured, clock, admitted, state.EnsureOwner, cancellationToken).ConfigureAwait(false)).Result;

    public async Task SettleOriginalResultAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult originalResult, CancellationToken cancellationToken)
        => _ = await NativeAnnOriginalOperationExecution.ExecuteAsync(database, principal, request, null,
            originalResult, configured, clock, admitted, state.EnsureOwner, cancellationToken,
            state.RequireOriginal(request)).ConfigureAwait(false);

    internal Task<(AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent)> ExecuteOriginalCapabilityAsync(
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, CancellationToken token, ReadExecutionBudget? originalBudget = null)
        => NativeAnnOriginalOperationExecution.ExecuteAsync(database, principal, request.Maintenance,
            request.OriginalConfigure, request.FullOriginalResult, configured, clock, admitted, state.EnsureOwner, token,
            request.Kind == AnnMaintenanceCapabilityKind.SettleOriginalResult ? state.RequireOriginal(request.Maintenance) : null, originalBudget);

    internal void RequireOriginalCapabilitySession(PrincipalRecord principal, AnnMaintenanceCapabilityRequest request)
        => state.RequireSession(principal.Id, request);

    public Task<AnnProjectionPublication> ReadPublishedAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, CancellationToken cancellationToken)
        => NativeAnnProjectionPublicationRead.ReadAsync(database, principal, request, configured,
            clock, admitted, state.EnsureOwner, cancellationToken);

    public IAnnProjectionLease Acquire(IKeyValueView view, AnnProjectionSelection request, ReadExecutionBudget budget)
    {
        using var originalOperation = admitted.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
        budget.Check();
        return NativeAnnPublicAcquisition.Acquire(database, view, state.RequirePublicOwner(), request, configured, budget);
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
        NativeAnnOriginalCapabilityExecution.Require(request);
        if (request.Kind is not (AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult))
        {
            NativeAnnMaintenanceAdmission.Request(request.Maintenance, request.SessionId, database.Store.Identity.NodeId);
        }
        if (request.Kind == AnnMaintenanceCapabilityKind.Release)
        {
            using var release = new NativeMaintenanceSessionWork(configured.Core.DatabaseLimits, clock,
                expiry ?? clock.GetUtcNow().AddSeconds(configured.Core.DatabaseLimits.Value.QueryDeadlineSeconds), shutdown.Token);
            using var releaseStage = release.EnterStage(binding, token);
            var released = await ExecuteCoreAsync(principal, request, release, token).ConfigureAwait(false);
            release.Budget.CheckResult(released);
            return released;
        }
        var work = admitted.RequireWork(principal.Id, request, configured, clock, expiry, shutdown.Token);
        using var stage = work.EnterStage(binding, token);
        var result = await ExecuteCoreAsync(principal, request, work, token).ConfigureAwait(false);
        work.Budget.CheckResult(result);
        return result;
    }

    private async Task<AnnMaintenanceCapabilityResult> ExecuteCoreAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, NativeMaintenanceSessionWork work, CancellationToken token)
    {
        NativeAnnOriginalCapabilityExecution.Require(request);
        if (request.Kind is AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult)
        {
            return await NativeAnnOriginalCapabilityExecution.ExecuteAsync(this, principal, request, token, work.Budget).ConfigureAwait(false);
        }
        using var originalOperation = admitted.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
        token.ThrowIfCancellationRequested();
        if (!principal.ClusterAdministrator || principal.Revoked)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, NativeAnnProtocol.InvalidSource);
        }
        NativeAnnMaintenanceAdmission.Request(request.Maintenance, request.SessionId, database.Store.Identity.NodeId);
        if (request.Kind == AnnMaintenanceCapabilityKind.Release)
        {
            return await NativeAnnMaintenanceLifecycle.ReleaseAsync(database, principal, request, directory,
                state.GetOwner, state.EnsureOwner, token).ConfigureAwait(false);
        }
        if (request.Kind == AnnMaintenanceCapabilityKind.Begin)
        {
            return await state.BeginAsync(database, principal, request, configured, work).ConfigureAwait(false);
        }
        var stage = state.RequireStage(principal.Id, request);
        return await stage.Owner.RunAsync(stages => NativeAnnMaintenanceStage.Execute(database, stage.Owner,
            stages, stage.Session, principal, request, configured, token)).ConfigureAwait(false);
    }

    internal NativeAnnMaintenanceWork? ObserveWork(Guid id) => state.ObserveWork(id);
    public async Task AbortAsync(Guid id)
    {
        if (admitted.FindWork(id) is { } work)
        {
            await work.CancelAndJoinStagesAsync().ConfigureAwait(false);
            await state.AbortAsync(id).ConfigureAwait(false);
            admitted.RetireWork(id);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await state.DisposeAsync(CloseAdmissionsAsync).ConfigureAwait(false);
        admitted.RetireWork();
        shutdown.Dispose();
    }

    private async Task CloseAdmissionsAsync()
    {
        var joined = admitted.CloseAndJoinAsync();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(admitted.JoinStagesAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => joined, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
