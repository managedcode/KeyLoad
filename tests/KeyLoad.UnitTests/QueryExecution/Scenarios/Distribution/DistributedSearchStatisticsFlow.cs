using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Query.Search;
using KeyLoad.UnitTests.DocumentStorage;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class DistributedSearchStatisticsFlow
{
    private const string QueryText = "source destination";
    private const int IndependentOwners = 2;

    internal static DistributedTextWitness Capture(DocumentPatchAtomicityOwner owner,
        PartitionRef partition, PhysicalShardRecord placement, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        var grant = budget.CreateReadGrant(owner.Database.Limits.MaxQueryReadBytes / IndependentOwners,
            owner.Database.Limits.MaxScanRecords);
        return DistributedTextStatisticsLeaf.Execute(owner.Database, QueryFixture.Reader,
            Request(partition), placement, QueryFixture.Tenant,
            UnitExecutionOptions.QueryExecution(), budget, grant);
    }

    internal static DistributedTextStatistics Combine(DocumentPatchAtomicityOwner source,
        DistributedTextWitness first, DistributedTextWitness second, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(source.Database.OperationLimitsOptions, source.Database.EvaluationClock, token);
        return DistributedTextStatisticsMerge.Combine([first.Statistics, second.Statistics],
            UnitExecutionOptions.QueryExecution(), budget);
    }

    internal static RankedDocument[] Rank(DocumentPatchAtomicityOwner owner, DistributedTextWitness witness,
        DistributedTextStatistics statistics, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        var grant = budget.CreateReadGrant(owner.Database.Limits.MaxQueryReadBytes / IndependentOwners,
            owner.Database.Limits.MaxScanRecords);
        using var admission = owner.Database.AdmitQuery(token);
        using var charged = budget.EnterReadGrant(grant);
        return owner.Database.WithPartitionQueryFenceView(QueryFixture.Reader, witness.Partition,
            QueryFixture.Collection, grant, (view, principal, resource, digest) =>
            {
                DistributedTextWitnessValidation.Require(owner.Database, view, principal, resource, digest,
                    witness, witness.Partition, grant, budget);
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
