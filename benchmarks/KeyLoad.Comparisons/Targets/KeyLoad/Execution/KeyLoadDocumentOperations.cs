using System.Collections.Immutable;
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
        if (result.IsFailed)
        {
            var code = result.Problem.ErrorCode;
            if (scenario == Scenario.DocumentWrite && code == nameof(ErrorCode.RevisionConflict))
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
            }
            if (classifyMissingUpdate && scenario == Scenario.DocumentUpdate && code == nameof(ErrorCode.RevisionConflict))
            {
                var observed = await client.GetAsync(
                    new(partition, ComparisonIdentities.DocumentsCollection, document.Id), token);
                if (observed.IsSuccess && observed.Value.Document is null)
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

    /// <summary>Creates new documents in one atomic command, with the single create's conflict, copy and receipt checks.</summary>
    internal static async Task CreateAsync(KeyLoadClient client, PartitionRef partition,
        IReadOnlyList<BenchmarkDocument> documents, int expectedCopies, CancellationToken token)
    {
        var mutations = documents.Select(document => CreateMutation(Scenario.DocumentWrite, document)).ToImmutableArray();
        var result = await client.CommitAsync(new(Guid.NewGuid(), partition, mutations), token);
        if (result.IsFailed && result.Problem.ErrorCode == nameof(ErrorCode.RevisionConflict))
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
        }
        var receipt = KeyLoadClientResults.Success(result);
        KeyLoadCopyAcknowledgements.Require(receipt, expectedCopies);
        if (receipt.Mutations.Length != documents.Count)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
        for (var index = 0; index < documents.Count; index++)
        {
            var mutation = receipt.Mutations[index];
            if (mutation.Resource != ComparisonIdentities.DocumentsCollection || mutation.Id != documents[index].Id
                || mutation.Revision != KeyLoadWorkloadIdentities.CreatedDocumentRevision)
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
            }
        }
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
