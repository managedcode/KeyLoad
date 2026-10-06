using System.Threading.Channels;

namespace KeyLoad.Comparisons;

internal static class OpenLoopProducer
{
    internal static async Task RunAsync(ScaledOperationInputs inputs, ChannelWriter<OpenLoopWorkItem> writer, OpenLoopTimeline timeline, OpenLoopRunState state, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;
        const int AdjacentElementOffset = 1;
        const int SingleItemCount = 1;

        Exception? failure = null;
        var next = NoObservedItems;
        try
        {
            for (; next < OpenLoopRateContract.PlannedOperations; next++)
            {
                var due = timeline.DueTimestamp(next);
                await timeline.WaitUntilAsync(due, cancellationToken).ConfigureAwait(false);
                var decision = timeProvider.GetTimestamp();
                var nextDue = next + AdjacentElementOffset == OpenLoopRateContract.PlannedOperations
                    ? long.MaxValue : timeline.DueTimestamp(next + SingleItemCount);
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
            RecordFailedProduction(state, next, error, ref failure, timeProvider: timeProvider);
            throw;
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }

    private static void RecordUnofferedRemainder(OpenLoopRunState state, int next, TimeProvider timeProvider)
    {
        for (; next < OpenLoopRateContract.PlannedOperations; next++)
        {
            state.RecordNotOffered(next, null, timeProvider.GetTimestamp());
        }
    }

    private static void RecordFailedProduction(OpenLoopRunState state, int next, Exception primary,
        ref Exception? completionFailure, TimeProvider timeProvider)
    {
        completionFailure = primary;
        try
        {
            RecordUnofferedRemainder(state, next, timeProvider: timeProvider);
        }
        catch (Exception accountingFailure)
        {
            completionFailure = OpenLoopFailure.Combine(primary, [accountingFailure])!;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(completionFailure).Throw();
            throw;
        }
    }
}
