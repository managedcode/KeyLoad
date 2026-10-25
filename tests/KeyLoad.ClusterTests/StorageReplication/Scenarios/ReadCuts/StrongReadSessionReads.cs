using KeyLoad.ClusterTests.OrleansExecution;
using static KeyLoad.ClusterTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class StrongReadSessionReads
{
    internal static async Task HealthyAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        StrongReadSessionState state, CancellationToken token)
    {
        EntityRef[] references = [new(state.Partition, Collection, SentinelId),
            new(state.Partition, Collection, TargetId), new(state.Partition, Collection, AbsentId)];
        foreach (var reference in references)
        {
            foreach (var original in state.Commits)
            {
                await OneAsync(root, reader, state, reference, original.Receipt.Token, token);
            }
        }
    }
    private static async Task OneAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        StrongReadSessionState state, EntityRef reference, CommitToken minimum, CancellationToken token)
    {
        var expected = reference.Id == SentinelId
            ? new DocumentResult(state.Stored(SentinelId), CreatedRevision, SentinelJson, false, [])
            : reference.Id == TargetId ? state.Target : null;
        foreach (var route in Enum.GetValues<StrongReadSessionRoute>())
        {
            await StrongReadSessionCurrent.ObserveAsync(root, state, () => StrongReadSessionResponses.ReadAsync(
                reader, reference, minimum, expected, route, token), token);
        }
    }
    internal static async Task InvalidAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        StrongReadSessionState state, bool policyDenied, CancellationToken token)
    {
        var minimum = state.Commits.First().Receipt.Token;
        var failures = StrongReadSessionRefusals.Invalid(minimum);
        foreach (var id in new[] { TargetId, AbsentId })
        {
            foreach (var failure in policyDenied ? failures.Take(AuthorityPrecedenceVariants) : failures)
            {
                await RejectRoutesAsync(root, reader, state, new(state.Partition, Collection, id), failure.Token,
                    policyDenied ? ErrorCode.PermissionDenied : ErrorCode.TokenInvalidated,
                    policyDenied ? ScopeDenied : failure.Detail, token);
            }
        }
    }
    private static async Task RejectRoutesAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        StrongReadSessionState state, EntityRef reference, CommitToken minimum, ErrorCode code, string detail,
        CancellationToken token)
    {
        foreach (var route in Enum.GetValues<StrongReadSessionRoute>())
        {
            await StrongReadSessionCurrent.ObserveAsync(root, state, () => StrongReadSessionRefusals.CallAsync(
                reader, state, reference, minimum, code, detail, route, token), token);
        }
    }
}
