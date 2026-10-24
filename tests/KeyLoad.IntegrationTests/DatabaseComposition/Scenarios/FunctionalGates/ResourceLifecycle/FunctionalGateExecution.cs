using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateExecution
{
    private const int MaximumFailureCharacters = 8192;

    internal static async Task RunAsync(FunctionalGateContext[] contexts, CancellationToken token)
    {
        using var queue = new SemaphoreSlim(1, 1);
        await RunWavesAsync(contexts, queue, token).ConfigureAwait(false);
    }

    private static async Task RunWavesAsync(FunctionalGateContext[] contexts, SemaphoreSlim queue,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        var blockedModels = new bool[FunctionalGateProtocol.ModelCount];
        for (var wave = 0; wave < FunctionalGateProtocol.MaximumWaves; wave++)
        {
            var beforeCancellation = failures.Count;
            ServerFailureObserver.Observe(token.ThrowIfCancellationRequested, failures);
            if (failures.Count != beforeCancellation)
            {
                break;
            }
            if (wave >= FunctionalGateProtocol.ModelCount * FunctionalGateProtocol.ResourceCount
                && (failures.Count > 0
                    || contexts.Sum(context => context.CompletedOperations) >= FunctionalGateProtocol.MinimumOperations))
            {
                break;
            }
            await RunWaveAsync(contexts, wave, queue, blockedModels, failures, token).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => Console.Out.WriteLineAsync(
                $"Functional gate wave={wave} successfulOperations={contexts.Sum(context => context.CompletedOperations)}"), failures)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(contexts.Sum(context => context.CompletedOperations)).IsGreaterThanOrEqualTo(FunctionalGateProtocol.MinimumOperations);
        foreach (var context in contexts)
        {
            await Assert.That(context.SdkOperations).IsGreaterThan(0);
            await Assert.That(context.McpOperations).IsGreaterThan(0);
        }
    }

    private static async Task RunWaveAsync(FunctionalGateContext[] contexts, int wave, SemaphoreSlim queue,
        bool[] blockedModels, List<Exception> failures, CancellationToken token)
    {
        var tasks = new List<(int Model, Task OriginalTask)>(contexts.Length);
        for (var index = 0; index < contexts.Length; index++)
        {
            var model = (wave + index) % FunctionalGateProtocol.ModelCount;
            if (blockedModels[model])
            {
                continue;
            }
            var beforeStart = failures.Count;
            ServerFailureObserver.Observe(() => tasks.Add((model, StepAsync(contexts[index], model,
                checked(wave * contexts.Length + index),
                (wave / FunctionalGateProtocol.ModelCount) % FunctionalGateProtocol.ResourceCount, queue, token))), failures);
            if (failures.Count != beforeStart)
            {
                blockedModels[model] = true;
                await LogFailuresAsync(failures, beforeStart, wave, model).ConfigureAwait(false);
            }
        }
        await Task.WhenAll(tasks.Select(task => task.OriginalTask)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        foreach (var original in tasks)
        {
            var beforeObservation = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => original.OriginalTask, failures).ConfigureAwait(false);
            if (failures.Count != beforeObservation)
            {
                blockedModels[original.Model] = true;
                await LogFailuresAsync(failures, beforeObservation, wave, original.Model).ConfigureAwait(false);
            }
        }
    }

    private static async Task LogFailuresAsync(List<Exception> failures, int first, int wave, int model)
    {
        var originals = failures.Skip(first).ToArray();
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var message = $"Functional gate model={model} wave={wave}: {original}";
                await Console.Error.WriteLineAsync(message.Length <= MaximumFailureCharacters
                    ? message : message[..MaximumFailureCharacters]).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
    }

    private static Task StepAsync(FunctionalGateContext context, int model, int iteration,
        int resourceIndex, SemaphoreSlim queue, CancellationToken token)
        => model switch
        {
            0 => FunctionalGateDocuments.ExerciseAsync(context, iteration, token),
            1 => FunctionalGateRelational.ExerciseAsync(context, iteration, token),
            2 => FunctionalGateComposition.ExerciseAsync(context, iteration, token),
            3 => FunctionalGateEvents.ExerciseAsync(context, iteration, resourceIndex, token),
            4 => QueueAsync(context, iteration, resourceIndex, queue, token),
            5 => FunctionalGateFiles.ExerciseAsync(context, iteration, resourceIndex, token),
            6 => FunctionalGateGraphs.ExerciseAsync(context, iteration, token),
            7 => FunctionalGateSearch.ExerciseAsync(context, iteration, token),
            8 => FunctionalGateAnalytics.ExerciseAsync(context, iteration, token),
            9 => FunctionalGateUserAnalytics.ExerciseAsync(context, iteration, token),
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };

    private static async Task QueueAsync(FunctionalGateContext context, int iteration,
        int resourceIndex, SemaphoreSlim queue, CancellationToken token)
    {
        await queue.WaitAsync(token);
        try
        {
            await FunctionalGateQueues.ExerciseAsync(context, iteration, resourceIndex, token);
        }
        finally
        {
            queue.Release();
        }
    }
}
