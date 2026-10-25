using KeyLoad.Query;
using static KeyLoad.FunctionalTests.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationPolicyFlow
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        await QueueViewLeaseIsolationPolicy.SetAsync(state, false, DeniedEpoch);
        var queries = new QueryEngine(state.Owner.Database, UnitExecutionOptions.QueryExecution());
        await QueueViewLeaseIsolationRefusals.DeniedAsync(state, () => queries.Execute(Reader,
            QueueViewLeaseIsolationRequests.Sql(state), cancellationToken: state.Token), ErrorCode.PermissionDenied);
        await QueueViewLeaseIsolationRefusals.DeniedAsync(state, () => queries.ExecuteAst(Reader,
            QueueViewLeaseIsolationRequests.Ast(state), cancellationToken: state.Token), ErrorCode.PermissionDenied);
        await QueueViewLeaseIsolationClaims.ActiveReplayAsync(state);
        await QueueViewLeaseIsolationMessages.RequireAsync(state);
        await QueueViewLeaseIsolationPolicy.SetAsync(state, true, RepairedEpoch);
        await QueueViewLeaseIsolationReads.RunAsync(state);
    }
}
