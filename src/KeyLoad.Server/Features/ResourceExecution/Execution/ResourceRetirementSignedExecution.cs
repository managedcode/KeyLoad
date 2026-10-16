using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Executes one private retirement capability through the existing connection-owned native command/query path.</summary>
internal sealed class ResourceRetirementSignedExecution(OrleansNode node, PartitionHost partition,
    PhysicalOwnerStartupRequests authentication, IOptions<DatabaseLimits> limits, TimeProvider clock,
    ResourceRetirementWorkAdmission workAdmission)
{
    private const int NoResultBytes = 0;
    internal async Task RequireSourceAsync(string sourcePrincipalId, long sourcePolicyEpoch,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        work.Check();
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var principal = partition.Database.Store.Read(raw => partition.Database.Principal(work.CreateView(raw, metadata),
            sourcePrincipalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        if (principal.PolicyEpoch != sourcePolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }

        work.Check();
        token.ThrowIfCancellationRequested();
    }

    internal Task<ResourceRetirementReadReplyV1> ReadAsync(ResourceRetirementReadRequestV1 query,
        string sourcePrincipalId, long sourcePolicyEpoch, bool technicalPrincipal, ReadExecutionBudget work, CancellationToken token)
        => new ResourceRetirementSignedReads(node, partition, limits, clock, this)
            .ExecuteAsync(query, sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, token);

    internal async Task<ReplicatedOperation> IssueOperationAsync(Guid commandId, ResourceRetirementNativePhaseRequestV1 request,
        string sourcePrincipalId, long sourcePolicyEpoch, bool technicalPrincipal, ReadExecutionBudget work,
        CancellationToken token)
    {
        ResourceRetirementFrameReadBudget.RequireAvailable(work);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var principal = await ReadAdministratorAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, request.ExpiresAt, token).ConfigureAwait(false);
        if (technicalPrincipal && request.Phase?.Phase is (ResourceRetirementPhase.PersistDropParent
            or ResourceRetirementPhase.CompleteDropParent))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        RequireLocal(request, principal, work, token);
        var operation = ResourceRetirementNativePreparedOperations.Issue(partition.Database, principal, commandId, request, clock, work);
        principal = await ReadAdministratorAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, request.ExpiresAt, token).ConfigureAwait(false);
        if (technicalPrincipal && request.Phase?.Phase is (ResourceRetirementPhase.PersistDropParent
            or ResourceRetirementPhase.CompleteDropParent))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        RequireLocal(request, principal, work, token);
        token.ThrowIfCancellationRequested();
        return operation;
    }

    internal async Task<ResourceRetirementPhaseResultV1> PhaseAsync(Guid commandId, ResourceRetirementNativePhaseRequestV1 request,
        string sourcePrincipalId, long sourcePolicyEpoch, bool technicalPrincipal, ReadExecutionBudget work, CancellationToken token)
    {
        ResourceRetirementFrameReadBudget.RequireAvailable(work);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var principal = await ReadAdministratorAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, request.ExpiresAt, token).ConfigureAwait(false);
        if (technicalPrincipal && request.Phase?.Phase is (ResourceRetirementPhase.PersistDropParent
            or ResourceRetirementPhase.CompleteDropParent))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        RequireLocal(request, principal, work, token);
        var operation = ResourceRetirementNativePreparedOperations.ForSubmission(partition.Database, principal, commandId, request, clock, work);
        var value = await new ResourceRetirementCommandFrame(node, this).ExecuteAsync(principal, commandId,
            operation, request.ExpiresAt, work, token).ConfigureAwait(false);
        if (value.Receipt is not { } receipt || receipt.CommandId != commandId
            || request.Phase is not { } phase
            || receipt.RetirementId != phase.RetirementId || receipt.Phase != phase.Phase
            || receipt.PhysicalOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, request.ObservedOwner.Owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        ResourceRetirementPhaseReplyValidation.Require(phase, value, limits.Value);
        return value;
    }

    internal Task<PrincipalRecord> ReadAdministratorAsync(string sourcePrincipalId, long sourcePolicyEpoch,
        bool technicalPrincipal, ReadExecutionBudget work, DateTimeOffset originalExpiry, CancellationToken token)
        => new ResourceRetirementBudgetedAdministrator(partition, authentication, clock)
            .ReadAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, originalExpiry, token);

    internal void VerifyOwner(PhysicalShardRecord owner, ReadExecutionBudget work, CancellationToken token)
        => partition.Database.VerifyPhysicalCommandOwner(owner, false, work, token);

    private void RequireLocal(ResourceRetirementNativePhaseRequestV1 request, PrincipalRecord principal,
        ReadExecutionBudget work, CancellationToken token)
    {
        RequireLocal(request, principal, () => VerifyOwner(request.ObservedOwner.Owner, work, token));
    }

    private void RequireLocal(ResourceRetirementNativePhaseRequestV1 request, PrincipalRecord principal, Action verifyOwner)
    {
        if (request is null || request.Version != ResourceRetirementNativeProtocol.Version || request.Phase is null
            || request.Phase.Version != ResourceRetirementNativeProtocol.Version || request.Phase.RetirementId == Guid.Empty
            || !Enum.IsDefined(request.Phase.Phase) || request.Phase.Body.IsEmpty || request.Phase.Body.Length > limits.Value.MaxBatchBytes
            || request.MaximumReadBytes <= ResourceRetirementReadContract.NoReadBytes || request.MaximumReadBytes > limits.Value.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= ResourceRetirementReadContract.NoRecords || request.MaximumExaminedRecords > limits.Value.MaxScanRecords
            || request.MaximumResultBytes <= NoResultBytes || request.MaximumResultBytes > limits.Value.MaxBatchBytes
            || request.OriginalPolicyEpoch < ResourceRetirementReadContract.MinimumPolicyEpoch || !PhysicalOwnerEntryValidation.Valid(request.ObservedOwner))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }
        if (request.OriginalPolicyEpoch != ResourceRetirementReadContract.MinimumPolicyEpoch && request.OriginalPolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        if (request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }
        var actual = node.Discovery?.Read();
        if (actual is null || !actual.TransportReady || actual.VoterId != request.DestinationVoter
            || actual.SiloAddress != request.DestinationSiloAddress || actual.Incarnation != request.ObservedOwner.Owner.Incarnation
            || !request.ObservedOwner.Owner.VoterIds.Contains(actual.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        }
        verifyOwner();
        workAdmission.Require(request.Phase);
        if (NativeSerialization.Measure(request) > limits.Value.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementNativeProtocol.Unavailable);
        }
    }

    internal Task<GrainOperationReply> ExecuteAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, string signed, bool command, CancellationToken token)
        => ExecuteAsync(principal, requestId, commandId, signed, command, null, token);

    internal async Task<GrainOperationReply> ExecuteAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, string signed, bool command, Action? beforeDispatch, CancellationToken token)
    {
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, requestId, commandId, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                beforeDispatch?.Invoke();
                reply = await node.ExecuteAsync(requestId, signed, command, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
    }

    internal static T Value<T>(GrainOperationReply reply, CancellationToken token) where T : class
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, ResourceRetirementNativeProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as T
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
    }
}
