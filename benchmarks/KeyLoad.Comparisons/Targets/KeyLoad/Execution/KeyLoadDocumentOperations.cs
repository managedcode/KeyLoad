using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadDocumentOperations
{

    internal static Mutation CreateMutation(Scenario scenario, BenchmarkDocument document) => scenario switch
    {
        Scenario.DocumentWrite => new PutDocument(ComparisonIdentities.DocumentsCollection,
            document.Id, document.Json, KeyLoadWorkloadIdentities.InitialDocumentRevision),
        Scenario.DocumentUpdate => new PutDocument(ComparisonIdentities.DocumentsCollection,
            document.Id, document.Json, KeyLoadWorkloadIdentities.CreatedDocumentRevision, ExplicitReplacement: true),
        Scenario.DocumentDelete => new DeleteDocument(ComparisonIdentities.DocumentsCollection,
            document.Id, KeyLoadWorkloadIdentities.CreatedDocumentRevision),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    internal static async Task<OperationResult> ExecuteAsync(KeyLoadClient client, PartitionRef partition,
        Scenario scenario, BenchmarkDocument document, int expectedCopies, CancellationToken token, bool classifyMissingUpdate = true)
    {
        var result = await client.CommitAsync(new(Guid.NewGuid(), partition, [CreateMutation(scenario, document)]), token);
        if (!result.IsSuccess)
        {
            var code = result.Problem?.ErrorCode;
            if (scenario == Scenario.DocumentWrite && code == nameof(ErrorCode.RevisionConflict))
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
            }
            if (classifyMissingUpdate && scenario == Scenario.DocumentUpdate && code == nameof(ErrorCode.RevisionConflict))
            {
                var observed = await client.GetAsync(
                    new(partition, ComparisonIdentities.DocumentsCollection, document.Id), token);
                if (observed.IsSuccess && observed.Value is null)
                {
                    throw new ComparisonFailureException(ComparisonMutationFailures.UpdateMissing);
                }
            }
            if (scenario == Scenario.DocumentDelete && code == nameof(ErrorCode.NotFound))
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.DeleteMissing);
            }
        }

        var receipt = KeyLoadClientResults.Success(result);
        KeyLoadCopyAcknowledgements.Require(receipt, expectedCopies);
        ValidateReceipt(receipt, scenario, document);
        return new();
    }

    private static void ValidateReceipt(CommitReceipt receipt, Scenario scenario, BenchmarkDocument document)
    {
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        var expectedRevision = scenario == Scenario.DocumentWrite
            ? KeyLoadWorkloadIdentities.CreatedDocumentRevision
            : KeyLoadWorkloadIdentities.UpdatedDocumentRevision;
        if (receipt.Mutations.Length != SingleItemCount || receipt.Mutations[FirstElementIndex].Resource != ComparisonIdentities.DocumentsCollection
            || receipt.Mutations[FirstElementIndex].Id != document.Id || receipt.Mutations[FirstElementIndex].Revision != expectedRevision)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
    }
}
