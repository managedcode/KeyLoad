using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed record ResourceRetirementWorkCaptureResult(RegisteredPhysicalOwnerV1 Owner,
    ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> Evidence, ResourceRetirementWorkOwnerReceiptV1? Acknowledged);

/// <summary>Captures all original calls before checking any complete all-voter result.</summary>
internal sealed class ResourceRetirementWorkCapture(ResourceRetirementWorkReads workReads)
{
    private const int First = 1;

    internal async Task<ImmutableArray<ResourceRetirementWorkCaptureResult>> ExecuteAsync(
        ResourceRetirementStateV1 state, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var captured = ImmutableArray.CreateBuilder<ResourceRetirementWorkCaptureResult>();
        var failures = new List<Exception>();
        var remaining = state.Control.Directory.Owners.Sum(owner => owner.Owner.VoterIds.Length);
        foreach (var owner in state.Control.Directory.Owners)
        {
            var original = state.OwnerReceipts.Single(receipt => PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, owner.Owner));
            var fence = new ResourceRetirementOwnerFenceV1(state.Control, original);
            var evidence = ImmutableArray.CreateBuilder<ResourceRetirementWorkJoinEvidenceV1>();
            foreach (var voter in owner.Owner.VoterIds)
            {
                var allocation = remaining;
                remaining -= First;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    evidence.Add(await workReads.ExecuteAsync(fence, owner, voter, null, principalId, epoch,
                        expiry, work, metadata, token, allocation).ConfigureAwait(false));
                }, failures).ConfigureAwait(false);
            }
            captured.Add(new(owner, evidence.ToImmutable(), null));
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return captured.Select(Resolve).ToImmutableArray();
    }

    internal async Task<ResourceRetirementWorkCaptureResult> ReadDurableAsync(ResourceRetirementWorkOwnerReceiptV1 owner,
        string principalId, long epoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var evidence = ImmutableArray.CreateBuilder<ResourceRetirementWorkJoinEvidenceV1>();
        var failures = new List<Exception>();
        var remaining = owner.Receiver.Owner.VoterIds.Length;
        foreach (var voter in owner.Receiver.Owner.VoterIds)
        {
            var allocation = remaining--;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                evidence.Add(await workReads.ExecuteAsync(owner.Fence, owner.Receiver, voter, null,
                    principalId, epoch, expiry, work, metadata, token, allocation).ConfigureAwait(false));
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var result = Resolve(new(owner.Receiver, evidence.ToImmutable(), null));
        if (!ResourceRetirementValidation.Same(result.Acknowledged, owner))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        return result;
    }

    internal static ResourceRetirementWorkCaptureResult Resolve(ResourceRetirementWorkCaptureResult captured)
    {
        ResourceRetirementWorkOwnerReceiptV1? acknowledged = null;
        var replies = captured.Evidence.Select(item => NativeSerialization.Deserialize<ResourceRetirementReadReplyV1>(
            NativeSerialization.Deserialize<ResourceRetirementNativeReply>(item.SignedNativeReply.Span).Payload.Span)).ToArray();
        if (replies.Any(reply => reply.AcknowledgedOwner is not null))
        {
            acknowledged = replies.First().AcknowledgedOwner
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            if (replies.Any(reply => !ResourceRetirementValidation.Same(reply.AcknowledgedOwner, acknowledged)))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
        return captured with
        {
            Acknowledged = acknowledged
        };
    }
}
