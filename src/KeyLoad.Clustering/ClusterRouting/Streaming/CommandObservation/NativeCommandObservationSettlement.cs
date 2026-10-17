namespace KeyLoad.Clustering;

/// <summary>Joins the exact command and reader before releasing their original observation resources.</summary>
internal sealed class NativeCommandObservationSettlement(NativeRequestWorkOwner owner,
    NativeCommandObservation state, NativeCommandObservationResources resources, Task initialized, Task preparing)
{
    private const int NoFailures = 0;
    private const int SingleFailure = 1;
    private readonly Lock failuresGate = new();
    private readonly List<Exception> originals = [];
    private readonly List<Exception> cleanup = [];

    internal void Record(Exception? original, Exception? failure)
    {
        Add(originals, original);
        Add(cleanup, failure);
    }

    internal async Task FinishAsync(Task start, Task reader, Task<NativeRequestWorkOriginalSettlement> command, TaskCompletionSource? commandStart)
    {
        await start.ConfigureAwait(false);
        await initialized.ConfigureAwait(false);
        Add(cleanup, await resources.CancelAsync().ConfigureAwait(false));
        await preparing.ConfigureAwait(false);
        commandStart?.TrySetResult();
        var commandOutcome = await command.ConfigureAwait(false);
        Add(originals, commandOutcome.OriginalFailure);
        foreach (var error in commandOutcome.CleanupFailures)
        {
            Add(cleanup, error);
        }
        await reader.ConfigureAwait(false);
        state.ClearOwnedRequest();
        Add(cleanup, await NativeCommandObservationResources.ObserveAsync(state.Lease.PrepareCompletionAsync).ConfigureAwait(false));
        Add(cleanup, await resources.DisposeAsync().ConfigureAwait(false));
        lock (failuresGate)
        {
            state.TerminalFailures = [.. originals, .. cleanup];
        }
        var cleanupFailure = Collapse(cleanup);
        var result = await state.Lease.CompleteAsync(Collapse(originals), cleanupFailure).ConfigureAwait(false);
        owner.MarkCommandObservationSettled(state);
        lock (failuresGate)
        {
            state.TerminalFailures = [.. originals, .. cleanup,
                .. result.CleanupFailures.Where(error => !ReferenceEquals(error, cleanupFailure))];
        }
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(
            FatalOriginal(), result.CleanupOrJoinFailure));
    }

    private Exception? FatalOriginal()
    {
        lock (failuresGate)
        {
            return originals.Select(ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal)
                .FirstOrDefault(error => error is not null);
        }
    }

    private Exception? Collapse(List<Exception> failures)
    {
        lock (failuresGate)
        {
            return failures.Count switch
            {
                NoFailures => null,
                SingleFailure => failures[NoFailures],
                _ => new AggregateException(failures)
            };
        }
    }

    private void Add(List<Exception> failures, Exception? error)
    {
        lock (failuresGate)
        {
            if (error is not null && !failures.Any(existing => ReferenceEquals(existing, error)))
            {
                failures.Add(error);
            }
        }
    }
}
