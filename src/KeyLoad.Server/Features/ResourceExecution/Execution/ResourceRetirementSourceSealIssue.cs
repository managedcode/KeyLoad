using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementSourceSealIssue
{
    internal static async Task<ResourceRetirementReadReplyV1> ExecuteAsync(PartitionHost partition,
        ResourceRetirementReadRequestV1 query, PrincipalRecord principal, IOptions<DatabaseLimits> limits, ReadExecutionBudget work,
        Func<Guid, ResourceRetirementNativePhaseRequestV1, Task<ReplicatedOperation>> issue)
    {
        ResourceRetirementSourceSealShape.Require(query, limits.Value);
        work.Check();
        var context = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.SealBarrier,
            query.Control, null, null);
        var actual = partition.Database.ReadResourceRetirementAttempt(principal.Id, context, query.Attempt!.AttemptId, work);
        if (!ResourceRetirementValidation.Same(actual, query.Attempt))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        var phase = new ResourceRetirementPhaseCommandV1(query.Version, query.Request!.RetirementId,
            ResourceRetirementPhase.SealBarrier, NativeSerialization.Serialize(query.SourceSeal!));
        var request = new ResourceRetirementNativePhaseRequestV1(query.Version, phase, query.ObservedOwner,
            query.DestinationVoter, query.DestinationSiloAddress, query.OriginalPolicyEpoch, query.ExpiresAt,
            work.RemainingReadGrantBytes, work.RemainingReadGrantRecords, work.MaximumResultBytes);
        var original = await issue(actual.OriginalCommandId, request).ConfigureAwait(false);
        work.Check();
        var reply = new ResourceRetirementReadReplyV1(null, null, null, work.ReadBytes, work.ConstrainedNativeRecords)
        {
            OriginalOperation = original
        };
        work.CheckResult(reply);
        return reply;
    }
}
