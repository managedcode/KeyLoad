using System.Collections.Immutable;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadScaledCorpusSeeder
{
    private const int BatchSize = 100;

    internal static async Task SeedAsync(KeyLoadClient client, PartitionRef partition,
        IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;
        const string SeedDocumentsToken = "SeedDocuments";
        const string ScaledCorpusSeedReceiptMismatchDetail = "ScaledCorpusSeedReceiptMismatch";

        foreach (var batch in documents.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mutations = batch.Select(document => (Mutation)new PutDocument(nameof(documents), document.Id, document.Json, NoObservedItems)).ToImmutableArray();
            var result = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, mutations), cancellationToken), SeedDocumentsToken);
            if (result.Mutations.Length != mutations.Length)
            {
                throw new ComparisonFailureException(ScaledCorpusSeedReceiptMismatchDetail);
            }
        }
    }
}
