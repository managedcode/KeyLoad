namespace KeyLoad.AppHost.Features.TestInfrastructure.Processes;

/// <summary>Owns bounded output observation and joins its cancellation wait after the original tasks settle.</summary>
internal static class LocalRf3OwnedProcessObservation
{
    private const int ObservedTaskCapacity = 4;

    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        const int Step = 1;
        const int CountInitialValue = 0;
        const int CompletionCount = 0;
        const int StartIndexValue = 0;
        const string MessageText = "Local RF3 image cleanup output exceeded its bound.";

        var buffer = new char[maximumCharacters + Step];
        var count = CountInitialValue;
        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count)).ConfigureAwait(false);
            if (read == CompletionCount)
            {
                return new string(buffer, StartIndexValue, count);
            }

            count += read;
        }
        throw new InvalidOperationException(MessageText);
    }

    internal static async Task ObserveAsync(Task exit, Task output, Task error, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        const int CompletionCount = 3;

        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var canceled = Task.Delay(Timeout.InfiniteTimeSpan, timeProvider, observation.Token);
        try
        {
            var observed = new HashSet<Task>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ObserveCompletedAsync(exit, observed).ConfigureAwait(false);
                await ObserveCompletedAsync(output, observed).ConfigureAwait(false);
                await ObserveCompletedAsync(error, observed).ConfigureAwait(false);
                if (observed.Count == CompletionCount)
                {
                    return;
                }

                var pending = new List<Task>(ObservedTaskCapacity) { canceled };
                if (!observed.Contains(exit))
                {
                    pending.Add(exit);
                }

                if (!observed.Contains(output))
                {
                    pending.Add(output);
                }

                if (!observed.Contains(error))
                {
                    pending.Add(error);
                }

                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                if (completed == canceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                await completed.ConfigureAwait(false);
            }
        }
        finally
        {
            await observation.CancelAsync().ConfigureAwait(false);
            await canceled.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static async Task ObserveCompletedAsync(Task original, HashSet<Task> observed)
    {
        if (!original.IsCompleted)
        {
            return;
        }

        await original.ConfigureAwait(false);
        observed.Add(original);
    }

}
