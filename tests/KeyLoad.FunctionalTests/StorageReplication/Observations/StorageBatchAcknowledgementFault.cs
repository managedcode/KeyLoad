namespace KeyLoad.FunctionalTests.StorageReplication;

internal enum StorageBatchAcknowledgementFailure
{
    Sequence,
    Decision,
    Prefix,
    MissingOutcome
}

/// <summary>Changes one actual post-commit transport response, then permits exact retry and healthy traffic.</summary>
internal sealed class StorageBatchAcknowledgementFault(Guid commandId, StorageBatchAcknowledgementFailure failure)
    : IOutgoingGrainCallFilter
{
    private int changed;

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        var selected = StorageBatchCallSelection.Selects(context, commandId);
        await context.Invoke();
        if (!selected)
        {
            return;
        }
        var reply = (StorageCopyBatchAcknowledgement)context.Result! /* KLD0041: The selected publication transport response has the exact generated StorageCopyBatchAcknowledgement contract. */;
        var original = failure == StorageBatchAcknowledgementFailure.MissingOutcome;
        if ((reply.Outcome is not null) != original || Interlocked.CompareExchange(ref changed, 1, 0) != 0)
        {
            return;
        }
        var first = reply.Applied[0];
        var replacement = failure switch
        {
            StorageBatchAcknowledgementFailure.Sequence => reply with { Applied = [first with { Sequence = first.Sequence + 1 }] },
            StorageBatchAcknowledgementFailure.Decision => reply with { Applied = [first with { Succeeded = false, Error = ErrorCode.Conflict }] },
            StorageBatchAcknowledgementFailure.Prefix => reply with { Applied = [] },
            _ => reply with { Outcome = null }
        };
        context.Result = replacement /* KLD0041: Inject this exact typed post-commit acknowledgement through the selected framework response slot to exercise unknown-outcome handling. */;
    }
}
