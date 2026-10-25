namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class BoundedText
{
    internal static async Task<(bool HasValue, string? Value)> ReadAsync(string path, int maximum, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int BoundaryValue = 1;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                budget.Settings.RuntimeReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var limit = Math.Min(maximum, budget.Remaining);
            if (limit < BoundaryValue || !stream.CanSeek || stream.Length > limit)
            {
                return (false, default(string?));
            }

            return ((string?)((await ReadStreamAsync(stream, limit, budget, token)).Value)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(string?));
        }
        catch (IOException) { return (false, default(string?)); }
        catch (UnauthorizedAccessException) { return (false, default(string?)); }
    }
    private static async Task<(bool HasValue, string? Value)> ReadStreamAsync(FileStream stream, int limit,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int TotalInitialValue = 0;
        const int IndexValue = 0;
        const int EmptyValue = 0;
        var buffer = new byte[limit];
        var total = TotalInitialValue;
        while (true)
        {
            if (total == limit)
            {
                if (stream.Length != total)
                {
                    return (false, default(string?));
                }

                budget.Charge(total);
                return (true, ((string?)(System.Text.Encoding.UTF8.GetString(buffer, IndexValue, total))));
            }
            var count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
            if (count == EmptyValue)
            {
                budget.Charge(total);
                return (true, ((string?)(System.Text.Encoding.UTF8.GetString(buffer, IndexValue, total))));
            }
            total += count;
            if (total > limit)
            {
                return (false, default(string?));
            }
        }
    }
}
