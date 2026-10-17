using System.Collections.Immutable;
using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadScaledCorpusSeeder
{
    internal static async Task SeedAsync(KeyLoadClient client, PartitionRef partition,
        IReadOnlyList<BenchmarkDocument> documents, IOptions<NativeComparisonExecutionOptions> executionOptions,
        CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;
        const string SeedDocumentsToken = "SeedDocuments";
        const string ScaledCorpusSeedReceiptMismatchDetail = "ScaledCorpusSeedReceiptMismatch";

        var batchSize = NativeComparisonExecutionOptions.Require(executionOptions).Value.KeyLoadDocumentSeedBatchSize;
        foreach (var batch in documents.Chunk(batchSize))
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
