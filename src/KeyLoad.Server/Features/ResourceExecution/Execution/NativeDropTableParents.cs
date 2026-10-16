using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableParents
{
    internal static async Task<ResourceRetirementDropParentV1> LoadOrPersistAsync(NativeDropTableContext context,
        ResourceRetirementDropParentLookupV1 lookup)
    {
        var reply = await ReadAsync(context, lookup).ConfigureAwait(false);
        if (reply.Parent is { } parent)
        {
            return parent;
        }
        var admitted = reply.AdmittedDrop
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        var result = await context.Phases.ExecuteAsync(admitted, ResourceRetirementPhase.PersistDropParent,
            new ResourceRetirementPersistDropParentBodyV1(lookup, admitted), context.Source, context.Source.Owner.PhysicalShardId,
            context.PrincipalId, context.PolicyEpoch, context.Expiry, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
        var saved = result.DropParent ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        if (!ResourceRetirementValidation.Same(saved.Lookup, lookup)
            || !ResourceRetirementValidation.Same(saved.RetirementRequest, admitted))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return saved;
    }

    private static async Task<ResourceRetirementReadReplyV1> ReadAsync(NativeDropTableContext context,
        ResourceRetirementDropParentLookupV1 lookup)
    {
        var work = context.Work;
        work.Check();
        await context.Client.RequireRetirementSourceAsync(context.PrincipalId, context.PolicyEpoch, work, context.Metadata, context.Token)
            .ConfigureAwait(false);
        var bytes = context.Metadata.RemainingBytes;
        var records = context.Metadata.RemainingRecords;
        if (bytes <= ResourceRetirementReadContract.NoReadBytes || records <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        var query = new ResourceRetirementReadRequestV1(ResourceRetirementNativeProtocol.Version,
            ResourceRetirementReadOperation.DropParent, null, null, Guid.Empty, ResourceRetirementReadContract.NoRecords,
            context.Expiry, context.Source, string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch,
            bytes, records, work.MaximumResultBytes)
        {
            ParentLookup = lookup
        };
        work.CheckResult(query);
        var reply = await context.Client.ReadRetirementAsync(context.Source, query, context.PrincipalId,
            context.PolicyEpoch, work, context.Metadata, context.Token).ConfigureAwait(false);
        work.CheckResult(reply);
        return reply;
    }
}
