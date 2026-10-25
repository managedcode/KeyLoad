namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class BoundedText
{
    internal static async Task<global::ManagedCode.Communication.Result<string?>> ReadAsync(string path, int maximum, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int BoundaryValue = 1;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                budget.Settings.RuntimeReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var limit = Math.Min(maximum, budget.Remaining);
            if (limit < BoundaryValue || !stream.CanSeek || stream.Length > limit)
            {
                return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadAsync));
            }

            return ((string?)((await ReadStreamAsync(stream, limit, budget, token)).Value)) is { } outcomeValue2 ? global::ManagedCode.Communication.Result<string?>.Succeed(outcomeValue2) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadAsync));
        }
        catch (IOException) { return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadAsync)); }
        catch (UnauthorizedAccessException) { return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadAsync)); }
    }
    private static async Task<global::ManagedCode.Communication.Result<string?>> ReadStreamAsync(FileStream stream, int limit,
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
                    return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadStreamAsync));
                }

                budget.Charge(total);
                return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(System.Text.Encoding.UTF8.GetString(buffer, IndexValue, total))));
            }
            var count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
            if (count == EmptyValue)
            {
                budget.Charge(total);
                return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(System.Text.Encoding.UTF8.GetString(buffer, IndexValue, total))));
            }
            total += count;
            if (total > limit)
            {
                return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadStreamAsync));
            }
        }
    }
}
