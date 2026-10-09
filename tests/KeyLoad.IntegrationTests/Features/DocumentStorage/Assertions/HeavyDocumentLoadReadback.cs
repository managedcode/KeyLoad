using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Untimed complete ordered native readback and stable original operation oracles.</summary>
internal static class HeavyDocumentLoadReadback
{
    private const int SentinelOrdinal = 0;
    private const int SentinelPageSize = 1;
    private const string CursorFailure = "Heavy RF3 cursor failed to advance.";
    private const string ExtraRecordFailure = "Heavy RF3 readback contains extra records.";
    private readonly record struct DocumentMarker;

    internal static EntityRef Sentinel(PartitionRef partition)
        => new(partition, McpDocumentProtocol.Collection, HeavyDocumentLoadProtocol.Id(SentinelOrdinal));
    internal static AstQueryRequest SentinelQuery(PartitionRef partition)
    {
        var id = HeavyDocumentLoadProtocol.Id(SentinelOrdinal);
        return KeyLoadQuery.From<DocumentMarker>(partition, McpDocumentProtocol.Collection, IntegrationClientOptions.Translation())
            .Where(row => QueryFunctions.DocumentId(row) == id).Take(SentinelPageSize).ToRequest(allowFullScan: true);
    }
    internal static async Task VerifyReceiptAsync(CommitReceipt receipt, CommandRequest command, int ordinal)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(McpDocumentProtocol.Collection);
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(HeavyDocumentLoadProtocol.PutKind);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(HeavyDocumentLoadProtocol.Id(ordinal));
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(HeavyDocumentLoadProtocol.CreatedRevision);
    }
    internal static async Task VerifySentinelAsync(DocumentResult? actual, PartitionRef partition)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Reference).IsEqualTo(Sentinel(partition));
        await Assert.That(actual.Revision).IsEqualTo(HeavyDocumentLoadProtocol.CreatedRevision);
        await Assert.That(actual.Json).IsEqualTo(HeavyDocumentLoadProtocol.Json(SentinelOrdinal));
    }
    internal static async Task VerifySentinelPageAsync(QueryPage page)
    {
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.Rows[0].EntityId).IsEqualTo(HeavyDocumentLoadProtocol.Id(SentinelOrdinal));
        await Assert.That(page.Rows[0].Json).IsEqualTo(HeavyDocumentLoadProtocol.Json(SentinelOrdinal));
        await Assert.That(page.Rows[0].Revision).IsEqualTo(HeavyDocumentLoadProtocol.CreatedRevision);
    }
    internal static async Task VerifyAsync(KeyLoadClient client, PartitionRef partition, CancellationToken token)
    {
        using var expectedDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var actualDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var query = KeyLoadQuery.From<DocumentMarker>(partition, McpDocumentProtocol.Collection, IntegrationClientOptions.Translation())
            .OrderBy(row => QueryFunctions.DocumentId(row)).Take(HeavyDocumentLoadProtocol.PageSize);
        string? cursor = null;
        var count = 0;
        do
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await client.QueryAsync(query, allowFullScan: true,
                cursor: cursor, cancellationToken: token).ConfigureAwait(false)).ConfigureAwait(false);
            foreach (var row in page.Rows)
            {
                token.ThrowIfCancellationRequested();
                if (count >= HeavyDocumentLoadProtocol.Records)
                { throw new InvalidOperationException(ExtraRecordFailure); }
                var id = HeavyDocumentLoadProtocol.Id(count);
                var json = HeavyDocumentLoadProtocol.Json(count);
                await Assert.That(row.EntityId).IsEqualTo(id);
                await Assert.That(row.Json).IsEqualTo(json);
                await Assert.That(row.Revision).IsEqualTo(HeavyDocumentLoadProtocol.CreatedRevision);
                expectedDigest.AppendData(HeavyDocumentLoadProtocol.DigestRecord(id, json));
                actualDigest.AppendData(HeavyDocumentLoadProtocol.DigestRecord(row.EntityId, row.Json));
                count++;
            }
            if (page.Cursor is not null && (page.Rows.IsEmpty || string.Equals(cursor, page.Cursor, StringComparison.Ordinal)))
            { throw new InvalidOperationException(CursorFailure); }
            cursor = page.Cursor;
        } while (cursor is not null);
        await Assert.That(count).IsEqualTo(HeavyDocumentLoadProtocol.Records);
        await Assert.That(Convert.ToHexString(actualDigest.GetHashAndReset()))
            .IsEqualTo(Convert.ToHexString(expectedDigest.GetHashAndReset()));
    }
}
