using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementCleanupIssue
{
    internal static async Task<ResourceRetirementReadReplyV1> ExecuteAsync(ResourceRetirementReadRequestV1 query,
        PrincipalRecord principal, IOptions<DatabaseLimits> limits, ReadExecutionBudget work,
        Func<Guid, ResourceRetirementNativePhaseRequestV1, Task<ReplicatedOperation>> issue, CancellationToken token)
    {
        ResourceRetirementCleanupIssueShape.Require(query, limits.Value);
        work.Check();
        var phase = new ResourceRetirementPhaseCommandV1(query.Version, query.Request!.RetirementId,
            ResourceRetirementPhase.CleanupPage, NativeSerialization.Serialize(query.CleanupPage!));
        var request = new ResourceRetirementNativePhaseRequestV1(query.Version, phase, query.ObservedOwner,
            query.DestinationVoter, query.DestinationSiloAddress, principal.PolicyEpoch, query.ExpiresAt,
            work.RemainingReadGrantBytes, work.RemainingReadGrantRecords, work.MaximumResultBytes);
        var original = await issue(query.Attempt!.OriginalCommandId, request).ConfigureAwait(false);
        work.Check();
        var reply = new ResourceRetirementReadReplyV1(null, null, null, work.ReadBytes, work.ConstrainedNativeRecords)
        {
            OriginalOperation = original
        };
        work.CheckResult(reply);
        return reply;
    }
}
