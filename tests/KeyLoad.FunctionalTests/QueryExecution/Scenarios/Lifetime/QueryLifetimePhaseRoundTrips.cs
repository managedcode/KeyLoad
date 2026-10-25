using System.Collections.Immutable;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryLifetimePhaseRoundTrips(QueryLifetimeFlow flow)
{
    private const string ScoreProfile = "distributed-canonical";
    private const double SingleTextScore = 1d / 61;
    private readonly DatabaseEngine database = flow.Fixture.Destination.Database;
    private readonly PartitionQueryLifetimeMetadata metadata = flow.Capture();
    private readonly IOptions<QueryExecutionOptions> options = UnitExecutionOptions.QueryExecution();
    private readonly ReadExecutionBudget budget = new(flow.Fixture.Destination.Database.EvaluationClock, CancellationToken.None);
    private readonly DateTimeOffset expiry = flow.Fixture.Destination.Database.EvaluationClock.GetUtcNow()
        + UnitExecutionOptions.OperationLifetime;
    private readonly SearchRequest search = new(QueryFixture.Partition,
        QueryFixture.Collection, TextField: QueryFixture.Title,
        Text: QueryLifetimeProtocol.Text, Limit: QueryLifetimeProtocol.Limit);

    internal static Task VerifyAsync(QueryLifetimeFlow flow)
        => new QueryLifetimePhaseRoundTrips(flow).RunAsync();

    private async Task RunAsync()
    {
        QueryResultBudgetPolicy.Constrain(budget, options.Value);
        var first = await ExecuteAsync(DistributedSearchPhase.Statistics);
        var witness = first.Witness;
        var digest = DistributedSearchRequestDigest.Create(search, budget);
        var scope = new GlobalBranchScope(ScoreProfile, digest,
            DistributedSearchStatisticsEpoch.Create(digest, [witness], options, budget));
        var windows = DistributedSearchWorkFactory.WindowIds([search.Partition], budget);
        var candidates = await ExecuteAsync(DistributedSearchPhase.Candidates, witness, witness.Statistics, scope, windows);
        var selected = DistributedSearchGlobalFusion.Select(search, [candidates.Candidates!.Text!], [],
            windows, scope, budget, out _);
        await Assert.That(selected.Length).IsEqualTo(QueryLifetimeProtocol.Limit);
        var projected = await ExecuteAsync(DistributedSearchPhase.Projection, witness, selected: selected);
        var hit = projected.Projection!.Hits.Single();
        var expected = new DocumentResult(flow.Fixture.ExpectedDestinationReference, QueryFixture.First,
            QueryFixture.ProjectedJson, true, [QueryFixture.Secret]);
        await Assert.That(OrleansSerialization.Serialize(hit.Document).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
        await Assert.That(hit.Score).IsEqualTo(SingleTextScore);
        var final = await ExecuteAsync(DistributedSearchPhase.Revalidate, witness);
        await Assert.That(OrleansSerialization.Serialize(final.Witness).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(witness))).IsTrue();
    }

    private async Task<DistributedSearchLeafResult> ExecuteAsync(DistributedSearchPhase phase,
        DistributedTextWitness? witness = null, DistributedTextStatistics? statistics = null,
        GlobalBranchScope? scope = null, ImmutableArray<string> windows = default,
        ImmutableArray<GlobalBranchCandidate> selected = default)
    {
        var work = DistributedSearchWorkFactory.Create(phase, [search.Partition], search,
            witness is null ? [] : [witness], statistics, scope, windows, selected, budget).Single();
        await Assert.That(work.Selected.IsDefault).IsFalse();
        var original = new DistributedSearchOwnedLeaf(QueryLifetimeProtocol.Version, phase, work.Search,
            flow.Fixture.DestinationOwner, QueryFixture.Tenant,
            long.MaxValue / QueryLifetimeProtocol.GrantDivisor,
            int.MaxValue / QueryLifetimeProtocol.GrantDivisor,
            work.MaxResultBytes, work.Witness, work.Statistics, work.Scope, work.SourceWindowId, work.Selected, metadata.Definition, metadata.Coverage, metadata.Principal);
        var bytes = OrleansSerialization.Serialize(original);
        var actual = OrleansSerialization.Deserialize<DistributedSearchOwnedLeaf>(bytes);
        await Assert.That(bytes.AsSpan().SequenceEqual(OrleansSerialization.Serialize(actual))).IsTrue();
        var missing = original with
        {
            Selected = default
        };
        await QueryLifetimeAssertions.RefusedAsync(() => _ = OrleansSerialization.Serialize(missing), ErrorCode.Corruption);
        await QueryLifetimeAssertions.RefusedAsync(() => DistributedSearchOwnedLeafValidation.Require(
            missing, options.Value, budget), ErrorCode.Corruption);
        if (phase != DistributedSearchPhase.Projection)
        {
            await Assert.That(actual.Selected.IsEmpty).IsTrue();
            var nonempty = original with
            {
                Selected = [new(QueryFixture.Reference, QueryFixture.First, SingleTextScore)]
            };
            var invalid = OrleansSerialization.Deserialize<DistributedSearchOwnedLeaf>(OrleansSerialization.Serialize(nonempty));
            await QueryLifetimeAssertions.RefusedAsync(() => DistributedSearchOwnedLeafValidation.Require(
                invalid, options.Value, budget), ErrorCode.Corruption);
        }
        return DistributedSearchOwnedLeafExecution.Execute(database, QueryFixture.Reader, actual,
            flow.Fixture.DestinationOwner, options, database.EvaluationClock, expiry, CancellationToken.None);
    }
}
