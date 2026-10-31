using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Relational;

/// <summary>
/// The relational scenario, shared by every relational database and KeyLoad. Rows <c>[0, ops)</c> get a new
/// amount, <c>[ops, 2·ops)</c> are deleted, and selects use only the stable rows after them.
/// </summary>
internal static class RelationalScenario
{
    internal const string Load = "load";
    internal const string PkSelect = "pk-select";
    internal const string RangeSelect = "range-select";
    internal const string Update = "update";
    internal const string Delete = "delete";
    internal const string Mixed = "mixed";
    internal const int RangeWidth = 100;
    private const int MixedShares = 10;
    private const decimal UpdatedBy = 1m;
    private const string WrongRow = "Wrong row for id ";
    private const string WrongRange = "Wrong range result starting at id ";

    internal static async Task<CaseResult> LoadAsync(IRelationalDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        if (settings.Records <= (2 * settings.Operations) + RangeWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Records, null);
        }
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertAsync(
                [.. Enumerable.Range(first, count).Select(id => OrderDataset.Create(id))], cancellation), token);
    }

    internal static Task<CaseResult> PkSelectAsync(IRelationalDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(PkSelect, settings.Operations, settings.Clients,
            (index, cancellation) => SelectAsync(driver, Stable(settings, index), cancellation), token);

    internal static Task<CaseResult> RangeSelectAsync(IRelationalDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(RangeSelect, settings.Operations, settings.Clients,
            (index, cancellation) => SelectRangeAsync(driver, Stable(settings, index), cancellation), token);

    internal static Task<CaseResult> UpdateAsync(IRelationalDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Update, settings.Operations, settings.Clients,
            (index, cancellation) => driver.UpdateAmountAsync(index, OrderDataset.Amount(index) + UpdatedBy, cancellation), token);

    internal static Task<CaseResult> DeleteAsync(IRelationalDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Delete, settings.Operations, settings.Clients,
            (index, cancellation) => driver.DeleteAsync(settings.Operations + index, cancellation), token);

    /// <summary>60% primary-key selects, 20% updates, 10% inserts of new ids and 10% range selects, interleaved.</summary>
    internal static Task<CaseResult> MixedAsync(IRelationalDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Mixed, settings.Operations, settings.Clients, (index, cancellation) =>
            (index % MixedShares) switch
            {
                < 6 => driver.SelectAsync(Stable(settings, index), cancellation),
                < 8 => driver.UpdateAmountAsync(Stable(settings, index), UpdatedBy, cancellation),
                8 => driver.InsertAsync([OrderDataset.Create(settings.Records + index)], cancellation),
                _ => SelectRangeAsync(driver, Stable(settings, index), cancellation)
            }, token);

    private static async Task SelectAsync(IRelationalDriver driver, long id, CancellationToken token)
    {
        var expected = OrderDataset.Create(id);
        if (!OrderDataset.Matches(await driver.SelectAsync(id, token), expected, expected.Amount))
        {
            throw new InvalidDataException(WrongRow + id);
        }
    }

    private static async Task SelectRangeAsync(IRelationalDriver driver, long from, CancellationToken token)
    {
        var found = await driver.SelectRangeAsync(from, from + RangeWidth - 1, token);
        if (found.Count != RangeWidth || !found.ToHashSet().SetEquals(Enumerable.Range(0, RangeWidth).Select(offset => from + offset)))
        {
            throw new InvalidDataException(WrongRange + from);
        }
    }

    private static long Stable(BenchmarkSettings settings, int index)
        => settings.Spread(index, 2 * settings.Operations, settings.Records - (2 * settings.Operations) - RangeWidth);
}
