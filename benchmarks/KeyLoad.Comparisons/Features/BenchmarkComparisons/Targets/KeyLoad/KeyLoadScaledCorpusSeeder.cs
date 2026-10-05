using System.Collections.Immutable;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadScaledCorpusSeeder
{
    private const int BatchSize = 100;

    internal static async Task SeedAsync(KeyLoadClient client, PartitionRef partition,
        IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken)
    {
        foreach (var batch in documents.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mutations = batch.Select(document => (Mutation)new PutDocument("documents", document.Id, document.Json, 0)).ToImmutableArray();
            var result = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, mutations), cancellationToken), "SeedDocuments");
            if (result.Mutations.Length != mutations.Length)
            {
                throw new ComparisonFailureException("ScaledCorpusSeedReceiptMismatch");
            }
        }
    }
}
