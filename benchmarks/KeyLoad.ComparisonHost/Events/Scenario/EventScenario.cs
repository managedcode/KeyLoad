using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Events;

/// <summary>The event store scenario, shared by every event store and KeyLoad.</summary>
internal static class EventScenario
{
    internal const string Load = "load";
    internal const string Append = "append";
    internal const string Read = "read";
    internal const string Mixed = "mixed";
    private const string WrongEvent = "Wrong event in stream ";

    internal static async Task<CaseResult> LoadAsync(IEventDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.AppendManyAsync(
                [.. Enumerable.Range(first, count).Select(number => EventDataset.Create(number, settings.Seed))], cancellation), token);
    }

    /// <summary>New streams after the loaded ones.</summary>
    internal static Task<CaseResult> AppendAsync(IEventDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Append, settings.Operations, settings.Clients,
            (index, cancellation) => driver.AppendAsync(EventDataset.Create(settings.Records + index, settings.Seed), cancellation), token);

    internal static Task<CaseResult> ReadAsync(IEventDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Read, settings.Operations, settings.Clients,
            (index, cancellation) => ReadStableAsync(driver, settings, index, cancellation), token);

    /// <summary>Half appends to new streams, half reads of loaded streams, interleaved.</summary>
    internal static Task<CaseResult> MixedAsync(IEventDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Mixed, settings.Operations, settings.Clients, (index, cancellation) => index % 2 == 0
            ? driver.AppendAsync(EventDataset.Create(settings.Records + settings.Operations + index, settings.Seed), cancellation)
            : ReadStableAsync(driver, settings, index, cancellation), token);

    private static async Task ReadStableAsync(IEventDriver driver, BenchmarkSettings settings, int index, CancellationToken token)
    {
        var number = settings.Spread(index, 0, settings.Records);
        if (!EventDataset.Matches(await driver.ReadFirstAsync(EventDataset.Stream(number), token), number, settings.Seed))
        {
            throw new InvalidDataException(WrongEvent + EventDataset.Stream(number));
        }
    }
}
