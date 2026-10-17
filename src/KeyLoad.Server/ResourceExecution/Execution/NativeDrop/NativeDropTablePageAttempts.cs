using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTablePageAttempts
{
    internal static async Task<ResourceRetirementAttemptV1?> CurrentAsync(NativeDropTableContext context,
        ResourceRetirementAttemptContextV1 scope)
    {
        var reply = await context.Reads.ExecuteAsync(ResourceRetirementReadOperation.Attempt,
            ResourceRetirementAttemptContextValidation.Request(scope), null, context.Source, Guid.Empty,
            ResourceRetirementReadContract.NoRecords, context.Expiry, context.PrincipalId, context.PolicyEpoch,
            context.Work, context.Metadata, context.Token, context: scope).ConfigureAwait(false);
        return reply.Attempt;
    }

    internal static async Task<ReplicatedOperation> IssueAsync(NativeDropTableContext context,
        ResourceRetirementAttemptContextV1 scope, ResourceRetirementCleanupPageBodyV1 body)
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
        var expiry = context.Expiry <= body.Attempt!.ExpiresAt ? context.Expiry : body.Attempt.ExpiresAt;
        var query = new ResourceRetirementReadRequestV1(ResourceRetirementNativeProtocol.Version,
            ResourceRetirementReadOperation.IssueCleanupPage, scope.Retirement!.Request, null, Guid.Empty,
            ResourceRetirementReadContract.NoRecords, expiry, body.Receiver, string.Empty, string.Empty,
            ResourceRetirementReadContract.MinimumPolicyEpoch, bytes, records, work.MaximumResultBytes)
        {
            Context = scope,
            Attempt = body.Attempt,
            CleanupPage = body
        };
        work.CheckResult(query);
        ResourceRetirementReadReplyV1? reply = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            reply = await context.Client.ReadRetirementAsync(body.Receiver, query, context.PrincipalId,
                context.PolicyEpoch, work, context.Metadata, context.Token).ConfigureAwait(false);
            work.CheckResult(reply);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply?.OriginalOperation
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
    }
}
