using KeyLoad.FunctionalTests.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class DistributedSearchStatisticsFlow
{
    private const string QueryText = "source destination";
    private const int IndependentOwners = 2;

    internal static DistributedTextWitness Capture(DocumentPatchAtomicityOwner owner,
        PartitionRef partition, PhysicalShardRecord placement, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(owner.Database.EvaluationClock, token);
        return DistributedTextStatisticsLeaf.Execute(owner.Database, QueryFixture.Reader,
            Request(partition), placement, QueryFixture.Tenant,
            UnitExecutionOptions.QueryExecution(), budget);
    }

    internal static DistributedTextStatistics Combine(DocumentPatchAtomicityOwner source,
        DistributedTextWitness first, DistributedTextWitness second, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(source.Database.EvaluationClock, token);
        return DistributedTextStatisticsMerge.Combine([first.Statistics, second.Statistics],
            UnitExecutionOptions.QueryExecution(), budget);
    }

    internal static RankedDocument[] Rank(DocumentPatchAtomicityOwner owner, DistributedTextWitness witness,
        DistributedTextStatistics statistics, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(owner.Database.EvaluationClock, token);
        using var admission = owner.Database.AdmitQuery(token);
        return owner.Database.WithPartitionQueryFenceView(QueryFixture.Reader, witness.Partition,
            QueryFixture.Collection, (view, principal, resource, digest) =>
            {
                DistributedTextWitnessValidation.Require(owner.Database, view, principal, resource, digest,
                    witness, witness.Partition, budget);
                owner.Database.Authorization.RequireFieldUse(principal, resource, QueryFixture.Title);
                var execution = UnitExecutionOptions.QueryExecution().Value;
                var request = Request(witness.Partition);
                var ranker = new TextRanker(request.Text!, request.TextField!, budget,
                    execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
                owner.Database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, ranker.Visit);
                var scores = ranker.Rank(statistics);
                return SearchBranchExecution.ProjectSelected(owner.Database, view, principal, resource, scores, budget);
            });
    }

    private static SearchRequest Request(PartitionRef partition)
        => new(partition, QueryFixture.Collection,
            TextField: QueryFixture.Title, Text: QueryText, Limit: IndependentOwners);
}
