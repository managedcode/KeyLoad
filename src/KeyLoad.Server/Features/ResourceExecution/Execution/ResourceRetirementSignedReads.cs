using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Accounts the owner frame and its existing receiver subcall on one original ledger.</summary>
internal sealed class ResourceRetirementSignedReads(OrleansNode node, PartitionHost partition,
    IOptions<DatabaseLimits> limits, TimeProvider clock, ResourceRetirementSignedExecution execution)
{
    internal async Task<ResourceRetirementReadReplyV1> ExecuteAsync(ResourceRetirementReadRequestV1 query,
        string sourcePrincipalId, long sourcePolicyEpoch, bool technicalPrincipal, ReadExecutionBudget work, CancellationToken token)
    {
        ResourceRetirementFrameReadBudget.RequireAvailable(work);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var principal = await execution.ReadAdministratorAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, query.ExpiresAt, token).ConfigureAwait(false);
        if ((query.Operation is ResourceRetirementReadOperation.DropParent or ResourceRetirementReadOperation.IssueSourceSeal)
            && technicalPrincipal)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }

        query = query with
        {
            OriginalPolicyEpoch = principal.PolicyEpoch
        };
        RequireShape(query, principal);
        execution.VerifyOwner(query.ObservedOwner.Owner, work, token);
        ResourceRetirementReadReplyV1 result;
        if (query.Operation is ResourceRetirementReadOperation.IssueCleanupPage
            or ResourceRetirementReadOperation.IssueSourceSeal or ResourceRetirementReadOperation.IssueOwnerReservation)
        {
            result = await IssueAsync(query, principal, sourcePrincipalId, sourcePolicyEpoch,
                technicalPrincipal, work, token).ConfigureAwait(false);
            principal = await execution.ReadAdministratorAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, query.ExpiresAt, token).ConfigureAwait(false);
            RequireShape(query, principal);
        }
        else
        {
            result = await ReceiverAsync(query, principal, work, token).ConfigureAwait(false);
            if (query.Operation == ResourceRetirementReadOperation.ActualOwnerWorkJoin)
            {
                principal = await execution.ReadAdministratorAsync(sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, query.ExpiresAt, token).ConfigureAwait(false);
                RequireShape(query, principal);
            }
        }
        result = ResourceRetirementFrameReadBudget.Complete(result, work);
        ResourceRetirementNativeReadValidation.RequireReply(query, result, limits.Value);
        return result;
    }

    private async Task<ResourceRetirementReadReplyV1> IssueAsync(ResourceRetirementReadRequestV1 query,
        PrincipalRecord principal, string sourcePrincipalId, long sourcePolicyEpoch, bool technicalPrincipal,
        ReadExecutionBudget work, CancellationToken token)
    {
        Task<ReplicatedOperation> Issue(Guid id, ResourceRetirementNativePhaseRequestV1 request)
            => execution.IssueOperationAsync(id, request, sourcePrincipalId, sourcePolicyEpoch, technicalPrincipal, work, token);
        if (query.Operation == ResourceRetirementReadOperation.IssueCleanupPage)
        {
            return await ResourceRetirementCleanupIssue.ExecuteAsync(query, principal, limits, work, Issue).ConfigureAwait(false);
        }

        if (query.Operation == ResourceRetirementReadOperation.IssueSourceSeal)
        {
            return await ResourceRetirementSourceSealIssue.ExecuteAsync(partition, query, principal, limits, work, Issue)
                .ConfigureAwait(false);
        }

        var body = new ResourceRetirementReserveOwnerBodyV1(query.Context!, query.Attempt!);
        var phase = new ResourceRetirementPhaseCommandV1(query.Version, query.Request!.RetirementId,
            ResourceRetirementPhase.ReserveOwner, NativeSerialization.Serialize(body));
        var request = new ResourceRetirementNativePhaseRequestV1(query.Version, phase, query.ObservedOwner,
            query.DestinationVoter, query.DestinationSiloAddress, query.OriginalPolicyEpoch, query.ExpiresAt,
            work.RemainingReadGrantBytes, work.RemainingReadGrantRecords, work.MaximumResultBytes);
        var original = await Issue(query.Attempt!.OriginalCommandId, request).ConfigureAwait(false);
        work.Check();
        return new(null, null, null, work.ReadBytes, work.ConstrainedNativeRecords)
        {
            OriginalOperation = original
        };
    }

    private async Task<ResourceRetirementReadReplyV1> ReceiverAsync(ResourceRetirementReadRequestV1 query,
        PrincipalRecord principal, ReadExecutionBudget work, CancellationToken token)
    {
        ResourceRetirementFrameReadBudget.RequireAvailable(work);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var child = query with
        {
            MaximumReadBytes = grant.RemainingBytes,
            MaximumExaminedRecords = grant.RemainingRecords
        };
        var failures = new List<Exception>();
        ResourceRetirementReadReplyV1? result = null;
        var begun = false;
        var imported = false;
        void Begin() => begun = true;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                result = query.Operation == ResourceRetirementReadOperation.ActualOwnerWorkJoin
                    ? await ResourceRetirementWorkRead.ExecuteAsync(node, partition, child, principal, limits, clock, Begin, token)
                        .ConfigureAwait(false)
                    : await ReadNativeAsync(child, principal, Begin, token).ConfigureAwait(false);
                ResourceRetirementNativeReadValidation.RequireReply(child, result, limits.Value);
                work.ImportSettledReadGrant(grant, result.ReadBytes, result.ExaminedRecords);
                imported = true;
            }
            catch (Exception original)
            {
                if (begun && !imported)
                {
                    work.FailIndeterminateMetadata(original);
                }

                throw;
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
    }

    private async Task<ResourceRetirementReadReplyV1> ReadNativeAsync(ResourceRetirementReadRequestV1 query,
        PrincipalRecord principal, Action beforeDispatch, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(query);
        string signed;
        try
        {
            signed = node.CatalogRequestCodec().CreateResourceRetirementRead(requestId, principal.Id, payload, query.ExpiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
        var reply = await execution.ExecuteAsync(principal, requestId, Guid.Empty, signed, false, beforeDispatch, token).ConfigureAwait(false);
        var value = ResourceRetirementSignedExecution.Value<ResourceRetirementReadReplyV1>(reply, token);
        return SourceNativeOwnerStatus.Stamp(value, partition, node.Discovery!.Read(), query.MaximumResultBytes, limits.Value);
    }

    private void RequireShape(ResourceRetirementReadRequestV1 query, PrincipalRecord principal)
    {
        var actual = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementReadShape.Require(query, principal, actual, limits.Value, clock, query.ExpiresAt);
    }
}
