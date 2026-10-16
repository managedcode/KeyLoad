using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableProgress
{
    internal static async Task<ResourceRetirementReadReplyV1> ReadAsync(NativeDropTableContext context,
        ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 receiver, PartitionRef? partition, bool release)
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
            release ? ResourceRetirementReadOperation.OwnerRelease : ResourceRetirementReadOperation.CleanupProgress,
            control.Request, control, Guid.Empty, ResourceRetirementReadContract.NoRecords, context.Expiry,
            receiver, string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch,
            bytes, records, work.MaximumResultBytes)
        {
            CleanupPartition = partition
        };
        work.CheckResult(query);
        var reply = await context.Client.ReadRetirementAsync(receiver, query, context.PrincipalId,
            context.PolicyEpoch, work, context.Metadata, context.Token).ConfigureAwait(false);
        work.CheckResult(reply);
        return reply;
    }
}
