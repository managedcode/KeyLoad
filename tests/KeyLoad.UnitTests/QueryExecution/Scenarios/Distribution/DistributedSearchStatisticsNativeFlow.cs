using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchStatisticsNativeFlow
{
    private const string QueryText = "source destination";
    private const int IndependentOwners = 2;

    internal static DistributedTextWitnessV1 Capture(DocumentPatchAtomicityOwner owner,
        PartitionRef partition, PhysicalShardRecord placement, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        var grant = budget.CreateReadGrant(owner.Database.Limits.MaxQueryReadBytes / IndependentOwners,
            owner.Database.Limits.MaxScanRecords);
        return DistributedTextStatisticsLeaf.Execute(owner.Database, QueryNativeFixture.Reader,
            Request(partition), placement, QueryNativeFixture.Tenant,
            UnitExecutionOptions.QueryExecution(), budget, grant);
    }

    internal static DistributedTextStatisticsV1 Combine(DocumentPatchAtomicityOwner source,
        DistributedTextWitnessV1 first, DistributedTextWitnessV1 second, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(source.Database.OperationLimitsOptions, source.Database.EvaluationClock, token);
        return DistributedTextStatisticsMerge.Combine([first.Statistics, second.Statistics],
            UnitExecutionOptions.QueryExecution(), budget);
    }

    internal static RankedDocument[] Rank(DocumentPatchAtomicityOwner owner, DistributedTextWitnessV1 witness,
        DistributedTextStatisticsV1 statistics, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        var grant = budget.CreateReadGrant(owner.Database.Limits.MaxQueryReadBytes / IndependentOwners,
            owner.Database.Limits.MaxScanRecords);
        using var admission = owner.Database.AdmitQuery(token);
        using var charged = budget.EnterReadGrant(grant);
        return owner.Database.WithPartitionQueryFenceView(QueryNativeFixture.Reader, witness.Partition,
            QueryNativeFixture.Collection, grant, (view, principal, resource, digest) =>
            {
                DistributedTextWitnessValidation.Require(owner.Database, view, principal, resource, digest,
                    witness, witness.Partition, grant, budget);
                owner.Database.Authorization.RequireFieldUse(principal, resource, QueryNativeFixture.Title);
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
        => new(partition, QueryNativeFixture.Collection,
            TextField: QueryNativeFixture.Title, Text: QueryText, Limit: IndependentOwners);
}
