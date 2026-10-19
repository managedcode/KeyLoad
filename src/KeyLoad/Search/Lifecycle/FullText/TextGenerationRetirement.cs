namespace KeyLoad.Server.Features.Search;

internal static class TextGenerationRetirement
{
    private const int NoReaderPins = 0;

    internal static void Adopt(TextResourceReservation? originalReservation, bool retiring, bool retired, ref TextResourceReservation? reservation)
    {
        if (reservation is not null || retiring || retired)
        {
            throw TextErrors.Ownership();
        }
        reservation = originalReservation;
    }

    internal static void Release(bool retired, int pins, ref TextResourceReservation? reservation)
    {
        if (!retired || pins != NoReaderPins)
        {
            throw TextErrors.Ownership();
        }
        reservation?.CompleteAfterJoinedCleanup();
        reservation = null;
    }

    internal static Task Mark(Exception? readerFailure, int pins, ref bool retiring, ref TaskCompletionSource? quiet)
    {
        retiring = true;
        if (readerFailure is not null)
        {
            return Task.FromException(readerFailure);
        }
        if (pins == NoReaderPins)
        {
            return Task.CompletedTask;
        }
        quiet ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        return quiet.Task;
    }

    internal static void Dispose(TextSelectedIndexSlot index, Exception? readerFailure, bool retiring, int pins, int readerCount, ref bool retired)
    {
        if (retired)
        {
            return;
        }
        if (readerFailure is not null)
        {
            throw new AggregateException(readerFailure);
        }
        if (!retiring || pins != NoReaderPins || readerCount != NoReaderPins)
        {
            throw TextErrors.Ownership();
        }
        index.RequireSettled();
        index.Dispose();
        retired = true;
    }

    internal static TextOnlineGenerationPin Pin(TextOnlineGeneration owner, KeyLoad.Core.ReadExecutionBudget budget, ref bool retiring, ref bool retired, ref int pins)
    {
        budget.Check();
        if (retiring || retired)
        {
            throw TextErrors.Ownership();
        }
        pins++;
        return new(owner);
    }

    internal static void ReleasePin(ref int pins, ref TaskCompletionSource? quiet)
    {
        if (pins <= NoReaderPins)
        {
            throw TextErrors.Ownership();
        }
        pins--;
        if (pins == NoReaderPins)
        {
            quiet?.TrySetResult();
        }
    }
}
