using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Commits actual all-voter work evidence through the existing owner and source RF3 phase execution.</summary>
internal sealed class ResourceRetirementWorkBarrier(ResourceOwnerInventoryNativeClient client,
    ResourceOwnerInventoryNativeTrust trust, IOptions<DatabaseLimits> limits,
    IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    private const int NoRecords = 0;
    private readonly ResourceRetirementPhaseExecution phases = new(client);
    private readonly ResourceRetirementWorkReads workReads = new(client);

    internal async Task<ResourceRetirementWorkBarrierExecution> ExecuteAsync(ResourceRetirementStateV1 state,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var originals = ImmutableArray.CreateBuilder<Exception>();
        var failures = new List<Exception>();
        ResourceRetirementWorkBarrierReceiptV1? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            receipt = await ExecuteCoreAsync(state, source, principalId, epoch, expiry, work, metadata, originals, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count != NoRecords)
        {
            failures.InsertRange(NoRecords, originals);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return new(receipt ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable),
            originals.ToImmutable());
    }

    private async Task<ResourceRetirementWorkBarrierReceiptV1> ExecuteCoreAsync(ResourceRetirementStateV1 state,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, ImmutableArray<Exception>.Builder originals, CancellationToken token)
    {
        ResourceRetirementStateValidation.State(state, limits.Value);
        var current = await new ResourceRetirementReads(client).ExecuteAsync(ResourceRetirementReadOperation.State,
            state.Control.Request, null, source, Guid.Empty, NoRecords, expiry, principalId, epoch, work, metadata, token)
            .ConfigureAwait(false);
        var fresh = current.State ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        RequireSame(fresh.Control, state.Control);
        state = fresh;
        if (state.WorkBarrierReceipt is { } completed)
        {
            ResourceRetirementWorkValidation.RequireBarrier(state, limits.Value);
            return completed;
        }
        var originalSeal = await new ResourceRetirementReads(client).ExecuteAsync(ResourceRetirementReadOperation.OriginalD3Seal,
            state.Control.Request, state.Control, source, Guid.Empty, NoRecords, expiry, principalId, epoch, work, metadata, token)
            .ConfigureAwait(false);
        var sealedState = originalSeal.State
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        var capture = new ResourceRetirementWorkCapture(workReads);
        var captures = await capture.ExecuteAsync(state, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        foreach (var item in captures)
        {
            RequireCapture(item, state.Control);
        }
        foreach (var item in captures)
        {
            state = await RecordAsync(state, sealedState, source, item, capture, principalId, epoch, expiry, work, metadata, originals, token).ConfigureAwait(false);
        }
        foreach (var owner in state.WorkOwnerReceipts)
        {
            await ConsumeAsync(owner, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        }
        var sealedResult = await phases.ExecuteAsync(state.Control.Request, ResourceRetirementPhase.SealAllWork,
            new ResourceRetirementSealAllWorkBodyV1(state.Control), source, source.Owner.PhysicalShardId,
            principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        return sealedResult.WorkBarrierReceipt
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
    }

    private async Task<ResourceRetirementStateV1> RecordAsync(ResourceRetirementStateV1 state,
        ResourceRetirementStateV1 sealedState, RegisteredPhysicalOwnerV1 source, ResourceRetirementWorkCaptureResult item,
        ResourceRetirementWorkCapture capture, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, ImmutableArray<Exception>.Builder originals, CancellationToken token)
    {
        var existing = state.WorkOwnerReceipts.SingleOrDefault(receipt =>
            receipt.Receiver.Owner.PhysicalShardId == item.Owner.Owner.PhysicalShardId);
        if (existing is not null)
        {
            RequireSame(existing, item.Acknowledged);
            return state;
        }
        var owner = item.Acknowledged;
        if (owner is null)
        {
            var body = new ResourceRetirementCommitOwnerWorkJoinBodyV1(sealedState, item.Owner, item.Evidence);
            var original = item;
            var submitted = await ResourceRetirementOwnerWorkReconciliation.SubmitAsync(
                () => phases.ExecuteAsync(state.Control.Request, ResourceRetirementPhase.CommitOwnerWorkJoin, body,
                    original.Owner, original.Owner.Owner.PhysicalShardId, principalId, epoch, expiry, work, metadata, token),
                () => ResourceRetirementWorkOwnerCapture.ReadAsync(sealedState, original.Owner, workReads,
                    principalId, epoch, expiry, work, metadata, token), value => RequireCapture(value, state.Control), original)
                .ConfigureAwait(false);
            owner = submitted.Receipt;
            Add(submitted.OriginalSubmissionFailure, originals);
            item = submitted.DurableRead ?? await capture.ReadDurableAsync(owner, principalId, epoch, expiry, work, metadata, token)
                .ConfigureAwait(false);
            RequireCapture(item, state.Control);
        }
        var record = new ResourceRetirementRecordOwnerWorkJoinBodyV1(state.Control, owner) { DurableReads = item.Evidence };
        var recorded = await ResourceRetirementSourceWorkReconciliation.SubmitAsync(
            () => phases.ExecuteAsync(state.Control.Request, ResourceRetirementPhase.RecordOwnerWorkJoin, record,
                source, record.OwnerReceipt.Receiver.Owner.PhysicalShardId, principalId, epoch, expiry, work, metadata, token),
            () => ReadCurrentAsync(state.Control, source, principalId, epoch, expiry, work, metadata, token), owner, limits.Value)
            .ConfigureAwait(false);
        Add(recorded.OriginalSubmissionFailure, originals);
        return recorded.State;
    }

    private async Task<ResourceRetirementStateV1> ReadCurrentAsync(ResourceRetirementControlV1 control,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var read = await new ResourceRetirementReads(client).ExecuteAsync(ResourceRetirementReadOperation.State,
            control.Request, null, source, Guid.Empty, NoRecords, expiry, principalId, epoch, work, metadata, token).ConfigureAwait(false);
        var state = read.State ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        RequireSame(state.Control, control);
        return state;
    }

    private static void Add(Exception? original, ImmutableArray<Exception>.Builder originals)
    {
        if (original is not null)
        {
            originals.Add(original);
        }
    }

    private void RequireCapture(ResourceRetirementWorkCaptureResult item, ResourceRetirementControlV1 control)
    {
        if (item.Acknowledged is { } acknowledged)
        {
            ResourceRetirementWorkEvidenceValidation.RequireDurable(acknowledged, item.Evidence, trust, limits, routing, clock);
            RequireSame(acknowledged.Control, control);
        }
        else
        {
            ResourceRetirementWorkEvidenceValidation.Require(item.Evidence, control, item.Owner, trust, limits, routing, clock);
        }
    }

    private async Task ConsumeAsync(ResourceRetirementWorkOwnerReceiptV1 owner, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var failures = new List<Exception>();
        var remaining = owner.Receiver.Owner.VoterIds.Length;
        foreach (var voter in owner.Receiver.Owner.VoterIds)
        {
            var allocation = remaining--;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await workReads.ExecuteAsync(owner.Fence, owner.Receiver, voter, owner,
                    principalId, epoch, expiry, work, metadata, allocation, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void RequireSame<T>(T actual, T expected)
    {
        if (!ResourceRetirementValidation.Same(actual, expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
    }
}
