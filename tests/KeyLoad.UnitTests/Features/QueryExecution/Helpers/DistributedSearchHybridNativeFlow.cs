using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchHybridNativeFlow
{
    private const string SourceWindow = "source-owner";
    private const string DestinationWindow = "destination-owner";
    private const string ScoreProfile = "distributed-canonical-v1";
    private const int IndependentOwners = 2;

    internal static RankedDocument[] Execute(RemoteDocumentNativeFixture fixture, CancellationToken token)
    {
        var sourceRequest = DistributedSearchHybridNativeSeed.Request(RemotePartitionQueryNativeFlow.Local);
        var destinationRequest = DistributedSearchHybridNativeSeed.Request(RemoteDocumentNativeFixture.Partition);
        var source = Capture(fixture.Source, sourceRequest, PhysicalOwnerDirectoryWholeFlow.Control.Owner, token);
        var destination = Capture(fixture.Destination, destinationRequest, PhysicalOwnerDirectoryWholeFlow.Destination.Owner, token);
        var budget = Budget(fixture.Source, token);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(sourceRequest)));
        var epoch = DistributedSearchStatisticsEpoch.Create(digest, [destination, source], UnitExecutionOptions.QueryExecution(), budget);
        var scope = new GlobalBranchScope(ScoreProfile, digest, epoch);
        var statistics = DistributedTextStatisticsMerge.Combine([destination.Statistics, source.Statistics],
            UnitExecutionOptions.QueryExecution(), budget);
        var left = Candidates(fixture.Source, sourceRequest, source, statistics, scope, SourceWindow, token);
        var right = Candidates(fixture.Destination, destinationRequest, destination, statistics, scope, DestinationWindow, token);
        var selected = DistributedSearchGlobalFusion.Select(sourceRequest, [left.Text!, right.Text!],
            [left.Vector!, right.Vector!], [SourceWindow, DestinationWindow], scope,
            fixture.Source.Database.Limits, budget, out _);
        var sourceHits = Project(fixture.Source, sourceRequest, source, selected, token);
        var destinationHits = Project(fixture.Destination, destinationRequest, destination, selected, token);
        return selected.Select(candidate => sourceHits.Concat(destinationHits)
            .Single(hit => hit.Document.Reference == candidate.Reference)).ToArray();
    }

    private static DistributedTextWitnessV1 Capture(PhysicalShardCatalogFixture owner,
        SearchRequest request, PhysicalShardRecord placement, CancellationToken token)
    {
        var budget = Budget(owner, token);
        return DistributedTextStatisticsLeaf.Execute(owner.Database, RemoteDocumentNativeFixture.Reader, request,
            placement, RemoteDocumentNativeFixture.Tenant, UnitExecutionOptions.QueryExecution(), budget, Grant(owner, budget));
    }

    private static DistributedSearchCandidateLeafV1 Candidates(PhysicalShardCatalogFixture owner,
        SearchRequest request, DistributedTextWitnessV1 witness, DistributedTextStatisticsV1 statistics,
        GlobalBranchScope scope, string window, CancellationToken token)
    {
        var budget = Budget(owner, token);
        return DistributedSearchCandidateExecution.Execute(owner.Database, RemoteDocumentNativeFixture.Reader,
            request, RemoteDocumentNativeFixture.Tenant, witness, statistics, scope, window,
            UnitExecutionOptions.QueryExecution(), budget, Grant(owner, budget));
    }

    private static ImmutableArray<RankedDocument> Project(PhysicalShardCatalogFixture owner,
        SearchRequest request, DistributedTextWitnessV1 witness, ImmutableArray<GlobalBranchCandidate> selected,
        CancellationToken token)
    {
        var budget = Budget(owner, token);
        return DistributedSearchProjectionExecution.Execute(owner.Database, RemoteDocumentNativeFixture.Reader,
            request, RemoteDocumentNativeFixture.Tenant, witness,
            [.. selected.Where(candidate => candidate.Reference.Partition == request.Partition)],
            budget, Grant(owner, budget)).Hits;
    }

    private static ReadExecutionBudget Budget(PhysicalShardCatalogFixture owner, CancellationToken token)
        => new(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);

    private static global::KeyLoad.Core.Features.ResourceExecution.Execution.ReadExecutionBudgetReadGrant Grant(
        PhysicalShardCatalogFixture owner, ReadExecutionBudget budget)
        => budget.CreateReadGrant(owner.Database.Limits.MaxQueryReadBytes / IndependentOwners,
            owner.Database.Limits.MaxScanRecords);
}
