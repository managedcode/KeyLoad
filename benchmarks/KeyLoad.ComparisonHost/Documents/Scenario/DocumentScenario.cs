using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>
/// The documents scenario, shared by every document database and KeyLoad. Ids <c>[0, ops)</c> are updated,
/// <c>[ops, 2·ops)</c> deleted, and reads and range queries use only the stable ids after them.
/// </summary>
internal static class DocumentScenario
{
    internal const string Load = "load";
    internal const string PointRead = "point-read";
    internal const string RangeQuery = "range-query";
    internal const string Update = "update";
    internal const string Delete = "delete";
    internal const string Mixed = "mixed";
    internal const int RangeWidth = 10;
    private const int MixedShares = 10;
    private const string WrongDocument = "Wrong document body for ";
    private const string WrongRange = "Wrong range result starting at ";

    internal static async Task<CaseResult> LoadAsync(IDocumentDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        Validate(settings);
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, settings.Records, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertAsync(Documents(first, count), cancellation), token);
    }

    internal static Task<CaseResult> PointReadAsync(IDocumentDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(PointRead, settings.Operations, settings.Clients, settings.CaseLimit,
            (index, cancellation) => ReadAsync(driver, Stable(settings, index), cancellation), token);

    internal static Task<CaseResult> RangeQueryAsync(IDocumentDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(RangeQuery, settings.Operations, settings.Clients, settings.CaseLimit,
            (index, cancellation) => FindRangeAsync(driver, Stable(settings, index), cancellation), token);

    internal static Task<CaseResult> UpdateAsync(IDocumentDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Update, settings.Operations, settings.Clients, settings.CaseLimit,
            (index, cancellation) => driver.ReplaceAsync(DocumentDataset.Create(index, 1), cancellation), token);

    internal static Task<CaseResult> DeleteAsync(IDocumentDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Delete, settings.Operations, settings.Clients, settings.CaseLimit,
            (index, cancellation) => driver.DeleteAsync(DocumentDataset.Id(settings.Operations + index), cancellation), token);

    /// <summary>60% reads, 20% replacements, 10% inserts of new ids and 10% range queries, interleaved.</summary>
    internal static Task<CaseResult> MixedAsync(IDocumentDriver driver, BenchmarkSettings settings, CancellationToken token)
        => OperationLanes.RunAsync(Mixed, settings.Operations, settings.Clients, settings.CaseLimit, (index, cancellation) =>
            (index % MixedShares) switch
            {
                < 6 => ReadAsync(driver, Stable(settings, index), cancellation),
                < 8 => driver.ReplaceAsync(DocumentDataset.Create(Stable(settings, index), 2), cancellation),
                8 => driver.InsertAsync([DocumentDataset.Create(settings.Records + index)], cancellation),
                _ => FindRangeAsync(driver, Stable(settings, index), cancellation)
            }, token);

    private static async Task ReadAsync(IDocumentDriver driver, int number, CancellationToken token)
    {
        if (!DocumentDataset.Matches(await driver.ReadAsync(DocumentDataset.Id(number), token), number))
        {
            throw new InvalidDataException(WrongDocument + DocumentDataset.Id(number));
        }
    }

    private static async Task FindRangeAsync(IDocumentDriver driver, int from, CancellationToken token)
    {
        var found = await driver.FindRangeAsync(from, from + RangeWidth - 1, token);
        var expected = Enumerable.Range(from, RangeWidth).Select(DocumentDataset.Id);
        if (found.Count != RangeWidth || !found.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
        {
            throw new InvalidDataException(WrongRange + from);
        }
    }

    private static List<DocumentRecord> Documents(int first, int count)
        => [.. Enumerable.Range(first, count).Select(number => DocumentDataset.Create(number))];

    private static int Stable(BenchmarkSettings settings, int index)
        => settings.Spread(index, 2 * settings.Operations, settings.Records - (2 * settings.Operations) - RangeWidth);

    private static void Validate(BenchmarkSettings settings)
    {
        if (settings.Records <= (2 * settings.Operations) + RangeWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Records, null);
        }
    }
}
