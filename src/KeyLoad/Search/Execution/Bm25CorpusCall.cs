using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class Bm25CorpusCall
{
    private const int NoFailures = 0;
    private const int SingleFailureCount = 1;
    private const int OriginalFailureIndex = 0;

    internal static async Task<CommitReceipt> ExecuteAsync(OrleansNode node,
        PrincipalRecord principal, Guid requestId, Guid commandId, GrainRequestEnvelope envelope,
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
                reply = await node.ExecuteAsync(requestId, envelope, true, token);
            }, failures);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (failures.Count != NoFailures)
        {
            if (reply is { Error: null } && failures.All(CqrsBoundaryErrors.IsNonFatal))
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Bm25CorpusProtocol.Unavailable,
                failures.Count == SingleFailureCount ? failures[OriginalFailureIndex] : new AggregateException(failures));
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        if (reply is null)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Bm25CorpusProtocol.Unavailable);
        }
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? Bm25CorpusProtocol.Unavailable);
        }
        try
        {
            work.Check();
            token.ThrowIfCancellationRequested();
            if (((GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue corpus-publication reply slot once before validating its concrete receipt and command identity. */) is not CommitReceipt receipt /* KLD0042: Require the concrete CommitReceipt and original command ID from the current generated corpus-publication reply slot. */)
                || receipt.CommandId != commandId)
            {
                throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
            }
            return receipt;
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Bm25CorpusProtocol.Unavailable, failure);
        }
    }
}
