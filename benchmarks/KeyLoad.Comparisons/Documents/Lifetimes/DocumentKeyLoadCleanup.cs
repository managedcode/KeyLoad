using System.Collections.Immutable;
using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Purges one run's document namespace the way an application would: one cursor pass, then bounded deletes.</summary>
/// <remarks>Deleted documents remain as tombstones in the collection range, so re-querying from the start after every
/// delete batch would re-read every earlier tombstone. The identifiers are collected first, while no write invalidates
/// the cursor, and each delete batch then commits independently.</remarks>
internal static class DocumentKeyLoadCleanup
{
    private const string CursorDidNotAdvance = "DocumentCleanupCursorDidNotAdvance";

    private sealed record DocumentCleanupQueryMarker;

    internal static async Task RunAsync(KeyLoadClient client, PartitionRef partition,
        IOptions<ComparisonExecutionOptions> options, IOptions<QueryTranslationOptions> translationOptions, TimeProvider timeProvider)
    {
        var execution = ComparisonExecutionOptions.Require(options).Value;
        using var deadline = new CancellationTokenSource(execution.DocumentCleanupTimeout, timeProvider);
        var identifiers = await ReadIdentifiersAsync(client, partition, execution, translationOptions, deadline.Token).ConfigureAwait(false);
        foreach (var batch in identifiers.Chunk(execution.KeyLoadDocumentSeedBatchSize))
        {
            var deletes = batch.Select(id => (Mutation)new DeleteDocument(ComparisonIdentities.DocumentsCollection, id, null))
                .ToImmutableArray();
            _ = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, deletes), deadline.Token).ConfigureAwait(false));
        }
    }

    private static async Task<List<string>> ReadIdentifiersAsync(KeyLoadClient client, PartitionRef partition,
        ComparisonExecutionOptions execution, IOptions<QueryTranslationOptions> translationOptions, CancellationToken token)
    {
        var identifiers = new List<string>();
        string? cursor = null;
        do
        {
            var query = KeyLoadQuery.From<DocumentCleanupQueryMarker>(partition, ComparisonIdentities.DocumentsCollection, translationOptions)
                .OrderBy(row => QueryFunctions.DocumentId(row)).Take(execution.ReadbackBatchCapacity);
            var page = KeyLoadClientResults.Success(await client.QueryAsync(query, allowFullScan: true, cursor: cursor,
                cancellationToken: token).ConfigureAwait(false));
            if (page.Rows.IsEmpty && page.Cursor is not null)
            {
                throw new ComparisonFailureException(CursorDidNotAdvance);
            }
            identifiers.AddRange(page.Rows.Select(row => row.EntityId));
            cursor = page.Cursor;
        } while (cursor is not null);
        return identifiers;
    }
}
