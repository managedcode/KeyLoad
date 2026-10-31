using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.KeyValue;

/// <summary>
/// The key-value scenario, shared by every key-value store and KeyLoad. Keys <c>[0, ops)</c> are overwritten,
/// <c>[ops, 2·ops)</c> deleted, and reads use only the stable keys after them.
/// </summary>
internal static class KeyValueScenario
{
    internal const string Load = "load";
    internal const string Get = "get";
    internal const string Set = "set";
    internal const string Delete = "delete";
    internal const string Mixed = "mixed";
    private const int MixedShares = 10;
    private const int MixedReads = 8;
    private const string WrongValue = "Wrong value for ";

    internal static async Task<CaseResult> LoadAsync(IKeyValueDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        if (settings.Records <= 2 * settings.Operations)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Records, null);
        }
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.SetManyAsync(
                [.. Enumerable.Range(first, count).Select(number => KeyValueDataset.Create(number))], cancellation), token);
    }

    internal static Task<CaseResult> GetAsync(IKeyValueDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Get, settings.Operations, settings.Clients,
            (index, cancellation) => ReadAsync(driver, Stable(settings, index), cancellation), token);

    internal static Task<CaseResult> SetAsync(IKeyValueDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Set, settings.Operations, settings.Clients,
            (index, cancellation) => driver.SetAsync(KeyValueDataset.Create(index, 1), cancellation), token);

    internal static Task<CaseResult> DeleteAsync(IKeyValueDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Delete, settings.Operations, settings.Clients,
            (index, cancellation) => driver.DeleteAsync(KeyValueDataset.Key(settings.Operations + index), cancellation), token);

    /// <summary>80% reads and 20% overwrites of stable keys, interleaved.</summary>
    internal static Task<CaseResult> MixedAsync(IKeyValueDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Mixed, settings.Operations, settings.Clients, (index, cancellation) =>
            index % MixedShares < MixedReads
                ? ReadAsync(driver, Stable(settings, index), cancellation)
                : driver.SetAsync(KeyValueDataset.Create(Stable(settings, index), 2), cancellation), token);

    private static async Task ReadAsync(IKeyValueDriver driver, int number, CancellationToken token)
    {
        if (!KeyValueDataset.Matches(await driver.GetAsync(KeyValueDataset.Key(number), token), number))
        {
            throw new InvalidDataException(WrongValue + KeyValueDataset.Key(number));
        }
    }

    private static int Stable(BenchmarkSettings settings, int index)
        => settings.Spread(index, 2 * settings.Operations, settings.Records - (2 * settings.Operations));
}
