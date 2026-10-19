using System.Collections.Immutable;
using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class DocumentKeyLoadCleanup
{
    private sealed record DocumentCleanupQueryMarker;
    internal static async Task RunAsync(KeyLoadClient client, PartitionRef partition,
        IOptions<ComparisonExecutionOptions> options, IOptions<QueryTranslationOptions> translationOptions, TimeProvider timeProvider)
    {
        var execution = ComparisonExecutionOptions.Require(options).Value;
        using var deadline = new CancellationTokenSource(execution.DocumentCleanupTimeout, timeProvider);
        while (true)
        {
            var query = KeyLoadQuery.From<DocumentCleanupQueryMarker>(partition, ComparisonIdentities.DocumentsCollection, translationOptions)
                .OrderBy(row => QueryFunctions.DocumentId(row)).Take(Math.Min(execution.ReadbackBatchCapacity, execution.KeyLoadDocumentSeedBatchSize));
            var page = KeyLoadClientResults.Success(await client.QueryAsync(query, allowFullScan: true, cancellationToken: deadline.Token).ConfigureAwait(false));
            if (page.Rows.IsEmpty)
            {
                return;
            }

            var deletes = page.Rows.Select(row => (Mutation)new DeleteDocument(
                ComparisonIdentities.DocumentsCollection, row.EntityId, row.Revision)).ToImmutableArray();
            _ = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, deletes), deadline.Token).ConfigureAwait(false));
        }
    }
}
