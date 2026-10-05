using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisScaledCorpusSeeder
{
    private const int BatchSize = 256;

    internal static async Task SeedAsync(IDatabase database, string prefix, IReadOnlyList<BenchmarkDocument> documents,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < documents.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(BatchSize, documents.Count - offset);
            var batch = database.CreateBatch();
            var writes = new Task<bool>[count];
            for (var index = 0; index < count; index++)
            {
                var document = documents[offset + index];
                writes[index] = batch.StringSetAsync(prefix + document.Id, document.Json, flags: CommandFlags.DemandMaster);
            }
            batch.Execute();
            var receipts = await Task.WhenAll(writes).ConfigureAwait(false);
            if (receipts.Any(written => !written))
            {
                throw new ComparisonFailureException("ScaledCorpusSeedReceiptMismatch");
            }
        }
    }
}
