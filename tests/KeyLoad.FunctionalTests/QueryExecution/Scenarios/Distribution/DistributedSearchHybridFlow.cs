using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.FunctionalTests.DocumentStorage;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class DistributedSearchHybridFlow
{
    private const string SourceWindow = "source-owner";
    private const string DestinationWindow = "destination-owner";
    private const string ScoreProfile = "distributed-canonical";
    private const int IndependentOwners = 2;

    internal static RankedDocument[] Execute(QueryFixture fixture, CancellationToken token)
    {
        var sourceRequest = DistributedSearchHybridSeed.Request(QueryDataset.Local);
        var destinationRequest = DistributedSearchHybridSeed.Request(QueryFixture.Partition);
        var source = Capture(fixture.Source, sourceRequest, fixture.SourceOwner, token);
        var destination = Capture(fixture.Destination, destinationRequest, fixture.DestinationOwner, token);
        var budget = Budget(fixture.Source, token);
        var digest = Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(sourceRequest)));
        var epoch = DistributedSearchStatisticsEpoch.Create(digest, [destination, source], UnitExecutionOptions.QueryExecution(), budget);
        var scope = new GlobalBranchScope(ScoreProfile, digest, epoch);
        var statistics = DistributedTextStatisticsMerge.Combine([destination.Statistics, source.Statistics],
            UnitExecutionOptions.QueryExecution(), budget);
        var left = Candidates(fixture.Source, sourceRequest, source, statistics, scope, SourceWindow, token);
        var right = Candidates(fixture.Destination, destinationRequest, destination, statistics, scope, DestinationWindow, token);
        var selected = DistributedSearchGlobalFusion.Select(sourceRequest, [left.Text!, right.Text!],
            [left.Vector!, right.Vector!], [SourceWindow, DestinationWindow], scope, budget, out _);
        var sourceHits = Project(fixture.Source, sourceRequest, source, selected, token);
        var destinationHits = Project(fixture.Destination, destinationRequest, destination, selected, token);
        return selected.Select(candidate => sourceHits.Concat(destinationHits)
            .Single(hit => hit.Document.Reference == candidate.Reference)).ToArray();
    }

    private static DistributedTextWitness Capture(DocumentPatchAtomicityOwner owner,
        SearchRequest request, PhysicalShardRecord placement, CancellationToken token)
    {
        var budget = Budget(owner, token);
        return DistributedTextStatisticsLeaf.Execute(owner.Database, QueryFixture.Reader, request,
            placement, QueryFixture.Tenant, UnitExecutionOptions.QueryExecution(), budget, Grant(budget));
    }

    private static DistributedSearchCandidateLeaf Candidates(DocumentPatchAtomicityOwner owner,
        SearchRequest request, DistributedTextWitness witness, DistributedTextStatistics statistics,
        GlobalBranchScope scope, string window, CancellationToken token)
    {
        var budget = Budget(owner, token);
        return DistributedSearchCandidateExecution.Execute(owner.Database, QueryFixture.Reader,
            request, QueryFixture.Tenant, witness, statistics, scope, window,
            UnitExecutionOptions.QueryExecution(), budget, Grant(budget));
    }

    private static ImmutableArray<RankedDocument> Project(DocumentPatchAtomicityOwner owner,
        SearchRequest request, DistributedTextWitness witness, ImmutableArray<GlobalBranchCandidate> selected,
        CancellationToken token)
    {
        var budget = Budget(owner, token);
        return DistributedSearchProjectionExecution.Execute(owner.Database, QueryFixture.Reader,
            request, QueryFixture.Tenant, witness,
            [.. selected.Where(candidate => candidate.Reference.Partition == request.Partition)],
            budget, Grant(budget)).Hits;
    }

    private static ReadExecutionBudget Budget(DocumentPatchAtomicityOwner owner, CancellationToken token)
        => new(owner.Database.EvaluationClock, token);

    private static global::KeyLoad.Core.ResourceExecution.Execution.ReadExecutionBudgetReadGrant Grant(ReadExecutionBudget budget)
        => budget.CreateReadGrant(budget.RemainingReadGrantBytes / IndependentOwners, budget.RemainingReadGrantRecords);
}
