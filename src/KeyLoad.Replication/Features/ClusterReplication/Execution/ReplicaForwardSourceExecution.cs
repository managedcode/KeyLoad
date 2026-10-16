using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Replication;

/// <summary>Borrows one finite original planning grant for an authenticated forwarded submission.</summary>
internal static class ReplicaForwardSourceExecution
{
    private const string MissingResult = "The replica forward result is unavailable.";

    internal static Task<OperationResult> ExecuteAsync(ReplicaRpcClient rpc, string voter,
        ReplicatedOperation operation, ReadExecutionBudget? work, CancellationToken token)
        => work is null ? ExecuteLowerAsync(rpc, voter, operation, token)
            : ExecuteBoundAsync(rpc, voter, operation, work, token);

    private static async Task<OperationResult> ExecuteLowerAsync(ReplicaRpcClient rpc, string voter,
        ReplicatedOperation operation, CancellationToken token)
    {
        var request = new ReplicaForwardRequestV1(ReplicaForwardMetadataProtocol.CurrentVersion,
            ReplicaProtocolCodec.Serialize(operation), null, null);
        ReplicaForwardFrameValidation.RequireRequest(request);
        var reply = await rpc.InvokeAsync<ReplicaForwardRequestV1, ReplicaForwardReplyV1>(voter,
            ReplicaRpc.Forward, request, token).ConfigureAwait(false);
        ReplicaForwardFrameValidation.RequireReply(reply, budgeted: false);
        return ReadResult(reply);
    }

    private static async Task<OperationResult> ExecuteBoundAsync(ReplicaRpcClient rpc, string voter,
        ReplicatedOperation operation, ReadExecutionBudget work, CancellationToken token)
    {
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var attempted = false;
        var imported = false;
        var failures = new List<Exception>();
        OperationResult? result = null;
        try
        {
            var caps = ReplicaForwardReadBudget.Capture(work, grant);
            var expiry = work.ExpiresAt;
            var request = new ReplicaForwardRequestV1(ReplicaForwardMetadataProtocol.CurrentVersion,
                ReplicaProtocolCodec.Serialize(operation), caps, expiry);
            ReplicaForwardFrameValidation.RequireRequest(request);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, work.Cancellation);
            work.Check();
            var reply = await rpc.InvokeAsync<ReplicaForwardRequestV1, ReplicaForwardReplyV1>(voter,
                ReplicaRpc.Forward, request, () => attempted = true, linked.Token).ConfigureAwait(false);
            ReplicaForwardFrameValidation.RequireReply(reply, budgeted: true);
            var statistics = reply.Statistics ?? throw Errors.Fail(ErrorCode.Corruption, MissingResult);
            ReplicaForwardReadBudget.RequireStats(caps, statistics, reply.Result.Length);
            work.ImportSettledReadGrant(grant, statistics.ReadBytes, statistics.ExaminedRecords);
            imported = true;
            linked.Token.ThrowIfCancellationRequested();
            work.Check();
            result = ReadResult(reply);
        }
        catch (Exception failure)
        {
            if (attempted && !imported)
            {
                work.FailIndeterminateMetadata(failure);
            }
            failures.Add(failure);
        }
        if (!attempted || imported)
        {
            Complete(work, grant, failures);
        }
        ReplicaShutdownStage.ThrowIfAny(failures);
        work.Check();
        return result ?? throw Errors.Fail(ErrorCode.Corruption, MissingResult);
    }

    private static OperationResult ReadResult(ReplicaForwardReplyV1 reply)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? ReplicaProtocol.InterruptedWrite);
        }
        return ReplicaProtocolCodec.Deserialize<OperationResult>(reply.Result.Span);
    }

    private static void Complete(ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant,
        List<Exception> failures)
    {
        try
        {
            work.CompleteSettledReadGrant(grant);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }
}
