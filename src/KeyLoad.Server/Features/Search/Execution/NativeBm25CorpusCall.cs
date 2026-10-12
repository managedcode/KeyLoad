using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal static class NativeBm25CorpusCall
{
    private const int NoFailures = 0;
    private const int SingleFailureCount = 1;
    private const int OriginalFailureIndex = 0;

    internal static async Task<CommitReceipt> ExecuteAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, Guid requestId, Guid commandId, string signed,
        ReadExecutionBudget work, CancellationToken token)
    {
        GrainOperationReply? reply = null;
        var failures = new List<Exception>();
        try
        {
            using var identity = node.OpenRequestContext(principal, requestId, commandId, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                work.Check();
                token.ThrowIfCancellationRequested();
                reply = await node.ExecuteAsync(requestId, signed, true, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { failures.Add(failure); }
        if (failures.Count != NoFailures)
        {
            if (reply is { Error: null } && failures.All(NativeCqrsBoundaryErrors.IsNonFatal))
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Bm25CorpusProtocol.Unavailable,
                failures.Count == SingleFailureCount ? failures[OriginalFailureIndex] : new AggregateException(failures));
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        if (reply is null)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Bm25CorpusProtocol.Unavailable); }
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? Bm25CorpusProtocol.Unavailable); }
        try
        {
            work.Check();
            token.ThrowIfCancellationRequested();
            if (reply.Payload.Length > partition.Database.Limits.MaxBatchBytes
                || reply.Payload.Length > work.MaximumResultBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
            work.ChargeBytes(reply.Payload.Length);
            if (GrainNativePayload.Read<GrainValue>(reply.Payload).Value is not CommitReceipt receipt
                || receipt.CommandId != commandId)
            { throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid); }
            return receipt;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Bm25CorpusProtocol.Unavailable, failure); }
    }
}
