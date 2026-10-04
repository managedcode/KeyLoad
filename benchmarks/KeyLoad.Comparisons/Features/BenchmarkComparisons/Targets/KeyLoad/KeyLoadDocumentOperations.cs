using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadDocumentOperations
{
    private const string DocumentsCollection = "documents";

    internal static Mutation CreateMutation(Scenario scenario, BenchmarkDocument document) => scenario switch
    {
        Scenario.DocumentWrite => new PutDocument(DocumentsCollection, document.Id, document.Json, 0),
        Scenario.DocumentUpdate => new PutDocument(DocumentsCollection, document.Id, document.Json, 1, ExplicitReplacement: true),
        Scenario.DocumentDelete => new DeleteDocument(DocumentsCollection, document.Id, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    internal static async Task<OperationResult> ExecuteAsync(KeyLoadClient client, PartitionRef partition,
        Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        var result = await client.CommitAsync(new(Guid.NewGuid(), partition, [CreateMutation(scenario, document)]), token);
        if (!result.IsSuccess)
        {
            var code = result.Problem?.ErrorCode;
            if (scenario == Scenario.DocumentWrite && code == nameof(ErrorCode.RevisionConflict))
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
            }
            if (scenario == Scenario.DocumentUpdate && code == nameof(ErrorCode.RevisionConflict))
            {
                var observed = await client.GetAsync(new(partition, DocumentsCollection, document.Id), token);
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
        var expectedRevision = scenario == Scenario.DocumentWrite ? 1 : 2;
        if (receipt.Durability != DurabilityProfile.QuorumProcessDurable)
        {
            throw new ComparisonFailureException(KeyLoadEventOperations.WrongWriteProfile);
        }
        if (receipt.Mutations.Length != 1 || receipt.Mutations[0].Resource != DocumentsCollection
            || receipt.Mutations[0].Id != document.Id || receipt.Mutations[0].Revision != expectedRevision)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
        return new();
    }
}
