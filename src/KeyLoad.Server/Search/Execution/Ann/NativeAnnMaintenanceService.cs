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
        AnnMaintenanceCapabilityRequest request, CancellationToken cancellationToken)
        => ExecuteCoreAsync(principal, request, null, cancellationToken);

    public async Task<AnnMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, DateTimeOffset requestExpiry,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding is not INativeRequestWorkBinding native)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.InvalidSource);
        }
        var original = native.OriginalProducer.RequireOriginalParentBinding();
        var work = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, original.Cancellation);
        work.ConstrainLifetime(requestExpiry);
        work.AttachResourceLifetimeBinding(original);
        var result = await ExecuteCoreAsync(principal, request, work, cancellationToken).ConfigureAwait(false);
        work.CheckResult(result);
        return result;
    }

    private async Task<AnnMaintenanceCapabilityResult> ExecuteCoreAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, ReadExecutionBudget? originalBudget, CancellationToken token)
    {
        NativeAnnOriginalCapabilityExecution.Require(request);
        if (request.Kind is AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult)
        {
            return await NativeAnnOriginalCapabilityExecution.ExecuteAsync(this, principal, request, token, originalBudget).ConfigureAwait(false);
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
            return await state.BeginAsync(database, principal, request, configured, clock, originalBudget, token).ConfigureAwait(false);
        }
        var stage = state.RequireStage(principal.Id, request);
        return await stage.Owner.RunAsync(stages => NativeAnnMaintenanceStage.Execute(database, stage.Owner,
            stages, stage.Session, principal, request, configured, clock, token, originalBudget)).ConfigureAwait(false);
    }

    internal NativeAnnMaintenanceWork? ObserveWork(Guid id) => state.ObserveWork(id);
    public Task AbortAsync(Guid id) => state.AbortAsync(id);
    public ValueTask DisposeAsync() => state.DisposeAsync(admitted.CloseAndJoinAsync);
}
