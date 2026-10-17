namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class BoundedText
{
    internal static async Task<string?> ReadAsync(string path, int maximum, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int BoundaryValue = 1;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                budget.Settings.NativeReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var limit = Math.Min(maximum, budget.Remaining);
            if (limit < BoundaryValue || !stream.CanSeek || stream.Length > limit)
            {
                return null;
            }

            return await ReadStreamAsync(stream, limit, budget, token);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    private static async Task<string?> ReadStreamAsync(FileStream stream, int limit,
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
                    return null;
                }

                budget.Charge(total);
                return System.Text.Encoding.UTF8.GetString(buffer, IndexValue, total);
            }
            var count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
            if (count == EmptyValue)
            {
                budget.Charge(total);
                return System.Text.Encoding.UTF8.GetString(buffer, IndexValue, total);
            }
            total += count;
            if (total > limit)
            {
                return null;
            }
        }
    }
}
