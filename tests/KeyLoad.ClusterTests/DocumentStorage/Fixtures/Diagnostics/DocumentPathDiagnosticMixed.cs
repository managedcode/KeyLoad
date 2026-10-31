using KeyLoad.Client;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.DocumentStorage;

internal static class DocumentPathDiagnosticMixed
{
    private const int Pair = 2;
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;

    internal static Task ExecuteAsync(KeyLoadClient sdk, PartitionRef partition, int records, int nodes,
        CancellationToken token)
        => Parallel.ForEachAsync(Enumerable.Range(0, records), Parallelism(token), async (operation, cancellation) =>
        {
            var ordinal = (int)((long)operation * DocumentPathDiagnosticCorpus.ReadStride % records);
            if (ordinal % Pair != 0)
            {
                await ReadAsync(sdk, partition, ordinal, updated: false, cancellation);
                return;
            }
            var id = DocumentPathDiagnosticCorpus.Id(ordinal);
            var receipt = ClusterReplicationTestSupport.Success(await sdk.CommitAsync(new(Guid.NewGuid(), partition,
                [new PutDocument(DocumentPathDiagnosticCorpus.Collection, id,
                    DocumentPathDiagnosticCorpus.UpdatedJson(ordinal), InitialRevision)]), cancellation));
            if (receipt.PersistedCopies != nodes || receipt.Durability != DurabilityProfile.ProcessDurable
                || receipt.Mutations is not [{ Kind: "putDocument", Revision: UpdatedRevision } mutation]
                || mutation.Id != id || mutation.Resource != DocumentPathDiagnosticCorpus.Collection)
            {
                throw new InvalidOperationException("The diagnostic mixed acknowledgement changed its update or durable copies.");
            }
        });

    internal static Task VerifyAsync(KeyLoadClient sdk, PartitionRef partition, int records, CancellationToken token)
        => Parallel.ForEachAsync(Enumerable.Range(0, records), Parallelism(token), async (ordinal, cancellation) =>
            await ReadAsync(sdk, partition, ordinal, ordinal % Pair == 0, cancellation));

    private static async Task ReadAsync(KeyLoadClient sdk, PartitionRef partition, int ordinal, bool updated,
        CancellationToken token)
    {
        var reference = new EntityRef(partition, DocumentPathDiagnosticCorpus.Collection, DocumentPathDiagnosticCorpus.Id(ordinal));
        var result = ClusterReplicationTestSupport.Success(await sdk.GetAsync(reference, token));
        var document = result.Document ?? throw new InvalidOperationException("An acknowledged mixed diagnostic document is missing.");
        DocumentPathDiagnosticCorpus.Require(document.Reference.Id, document.Json, document.Revision, ordinal, updated);
    }

    private static ParallelOptions Parallelism(CancellationToken token)
        => new() { MaxDegreeOfParallelism = DocumentPathDiagnosticCorpus.Clients, CancellationToken = token };
}
