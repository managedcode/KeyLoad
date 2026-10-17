namespace KeyLoad.Server.Features.Search;

internal static class NativeTextGenerationRetirement
{
    private const int NoReaderPins = 0;

    internal static void Adopt(NativeTextResourceReservation? originalReservation, bool retiring, bool retired, ref NativeTextResourceReservation? reservation)
    {
        if (reservation is not null || retiring || retired)
        {
            throw NativeTextErrors.Ownership();
        }
        reservation = originalReservation;
    }

    internal static void Release(bool retired, int pins, ref NativeTextResourceReservation? reservation)
    {
        if (!retired || pins != NoReaderPins)
        {
            throw NativeTextErrors.Ownership();
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

    internal static void Dispose(NativeTextSelectedIndexSlot index, Exception? readerFailure, bool retiring, int pins, int readerCount, ref bool retired)
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
            throw NativeTextErrors.Ownership();
        }
        index.RequireSettled();
        index.Dispose();
        retired = true;
    }

    internal static NativeTextOnlineGenerationPin Pin(NativeTextOnlineGeneration owner, KeyLoad.Core.ReadExecutionBudget budget, ref bool retiring, ref bool retired, ref int pins)
    {
        budget.Check();
        if (retiring || retired)
        {
            throw NativeTextErrors.Ownership();
        }
        pins++;
        return new(owner);
    }

    internal static void ReleasePin(ref int pins, ref TaskCompletionSource? quiet)
    {
        if (pins <= NoReaderPins)
        {
            throw NativeTextErrors.Ownership();
        }
        pins--;
        if (pins == NoReaderPins)
        {
            quiet?.TrySetResult();
        }
    }
}
