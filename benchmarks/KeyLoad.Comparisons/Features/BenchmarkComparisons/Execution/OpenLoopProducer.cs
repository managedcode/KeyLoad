using System.Diagnostics;
using System.Threading.Channels;

namespace KeyLoad.Comparisons;

internal static class OpenLoopProducer
{
    internal static async Task RunAsync(ScaledOperationInputs inputs,
        ChannelWriter<OpenLoopWorkItem> writer, OpenLoopTimeline timeline, OpenLoopRunState state,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        var next = 0;
        try
        {
            for (; next < OpenLoopRateContract.PlannedOperations; next++)
            {
                var due = timeline.DueTimestamp(next);
                await timeline.WaitUntilAsync(due, cancellationToken).ConfigureAwait(false);
                var decision = Stopwatch.GetTimestamp();
                var nextDue = next + 1 == OpenLoopRateContract.PlannedOperations
                    ? long.MaxValue : timeline.DueTimestamp(next + 1);
                if (decision >= nextDue)
                {
                    state.RecordNotOffered(next, decision, decision);
                    continue;
                }
                var item = new OpenLoopWorkItem(next, state.SampleSlotFor(next), due,
                    timeline.DueOffsetNanoseconds(next), timeline.OperationDeadline(next), decision,
                    inputs.PayloadBytes);
                if (state.TryOffer(item, writer) == OpenLoopOfferDisposition.Frozen)
                {
                    state.RecordNotOffered(next, decision, decision);
                }
            }
        }
        catch (Exception error)
        {
            failure = error;
            for (; next < OpenLoopRateContract.PlannedOperations; next++)
            {
                state.RecordNotOffered(next, null, Stopwatch.GetTimestamp());
            }
        }
        finally
        {
            writer.TryComplete(failure);
        }
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
