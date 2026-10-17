using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableFlow
{
    internal static async Task<OperationResult> ExecuteAsync(NativeDropTableContext context,
        ResourceRetirementDropParentLookupV1 lookup)
    {
        var parent = await NativeDropTableParents.LoadOrPersistAsync(context, lookup).ConfigureAwait(false);
        if (parent.ResultJson is { } existing)
        {
            return NativeResult(existing);
        }
        var state = await context.ReadStateAsync(parent.RetirementRequest).ConfigureAwait(false);
        if (state is null || state.Stage == ResourceRetirementStage.Retiring)
        {
            var inventory = new ResourceOwnerInventoryAuthority(context.Partition, context.Client,
                context.Options.Node, context.Options.Core.DatabaseLimits, context.Clock);
            var authority = new ResourceRetirementAuthority(context.Partition, context.Client, inventory,
                context.Options.Node, context.Options.Core.DatabaseLimits, context.Clock);
            state = await authority.ExecuteAsync(context.PrincipalId, context.PolicyEpoch, parent.RetirementRequest,
                context.Options.Core.DatabaseLimits.Value.MaxResults, context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
        }
        if (state.WorkBarrierReceipt is null)
        {
            var trust = new ResourceOwnerInventoryNativeTrust(context.Partition, context.Options.Node,
                context.Options.Core.DatabaseLimits);
            var barrier = new ResourceRetirementWorkBarrier(context.Client, trust, context.Options.Core.DatabaseLimits,
                context.Options.GrainRouting, context.Clock);
            context.WorkExecution = await barrier.ExecuteAsync(state, context.Source, context.PrincipalId,
                context.PolicyEpoch, context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
            state = await context.ReadStateAsync(parent.RetirementRequest).ConfigureAwait(false)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            if (!ResourceRetirementValidation.Same(state.WorkBarrierReceipt, context.WorkExecution.Receipt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
            }
        }
        state = await NativeDropTableCompletion.ExecuteAsync(context, state).ConfigureAwait(false);
        ResourceRetirementCompletionValidation.RequireCompleted(state, context.Options.Core.DatabaseLimits.Value);
        var completed = await context.Phases.ExecuteAsync(parent.RetirementRequest, ResourceRetirementPhase.CompleteDropParent,
            new ResourceRetirementCompleteDropParentBodyV1(lookup), context.Source, context.Source.Owner.PhysicalShardId,
            context.PrincipalId, context.PolicyEpoch, context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
        var final = completed.DropParent
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        if (!ResourceRetirementValidation.Same(final.Lookup, lookup) || final.ResultJson is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        context.Work.Check();
        return NativeResult(final.ResultJson);
    }

    private static OperationResult NativeResult(string json)
    {
        try
        {
            return new(json)
            {
                NativeValue = JsonDefaults.Deserialize<DropTableResult>(json)
            };
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
    }
}
