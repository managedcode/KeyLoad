using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryLifetimeNativePhaseRoundTrips(QueryLifetimeNativeFlow flow)
{
    private const string ScoreProfile = "distributed-canonical-v1";
    private const double SingleTextScore = 1d / 61;
    private readonly DatabaseEngine database = flow.Fixture.Destination.Database;
    private readonly PartitionQueryLifetimeMetadata metadata = flow.Capture();
    private readonly IOptions<QueryExecutionOptions> options = UnitExecutionOptions.QueryExecution();
    private readonly ReadExecutionBudget budget = new(flow.Fixture.Destination.Database.OperationLimitsOptions,
        flow.Fixture.Destination.Database.EvaluationClock, CancellationToken.None);
    private readonly DateTimeOffset expiry = flow.Fixture.Destination.Database.EvaluationClock.GetUtcNow()
        + TimeSpan.FromSeconds(flow.Fixture.Destination.Database.Limits.QueryDeadlineSeconds);
    private readonly SearchRequest search = new(RemoteDocumentNativeFixture.Partition,
        RemoteDocumentNativeFixture.Collection, TextField: RemoteDocumentNativeFixture.Title,
        Text: QueryLifetimeNativeProtocol.Text, Limit: QueryLifetimeNativeProtocol.Limit);

    internal static Task VerifyAsync(QueryLifetimeNativeFlow flow)
        => new QueryLifetimeNativePhaseRoundTrips(flow).RunAsync();

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
            windows, scope, database.Limits, budget, out _);
        await Assert.That(selected.Length).IsEqualTo(QueryLifetimeNativeProtocol.Limit);
        var projected = await ExecuteAsync(DistributedSearchPhase.Projection, witness, selected: selected);
        var hit = projected.Projection!.Hits.Single();
        var expected = new DocumentResult(RemoteDocumentNativeFixture.Reference, RemoteDocumentNativeFixture.First,
            RemoteDocumentNativeFixture.ProjectedJson, true, [RemoteDocumentNativeFixture.Secret]);
        await Assert.That(NativeSerialization.Serialize(hit.Document).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(hit.Score).IsEqualTo(SingleTextScore);
        var final = await ExecuteAsync(DistributedSearchPhase.Revalidate, witness);
        await Assert.That(NativeSerialization.Serialize(final.Witness).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(witness))).IsTrue();
    }

    private async Task<DistributedSearchLeafResultV1> ExecuteAsync(DistributedSearchPhase phase,
        DistributedTextWitnessV1? witness = null, DistributedTextStatisticsV1? statistics = null,
        GlobalBranchScope? scope = null, ImmutableArray<string> windows = default,
        ImmutableArray<GlobalBranchCandidate> selected = default)
    {
        var work = DistributedSearchWorkFactory.Create(phase, [search.Partition], search,
            witness is null ? [] : [witness], statistics, scope, windows, selected, budget).Single();
        await Assert.That(work.Selected.IsDefault).IsFalse();
        var original = new DistributedSearchOwnedLeafV1(QueryLifetimeNativeProtocol.Version, phase, work.Search,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, RemoteDocumentNativeFixture.Tenant,
            database.Limits.MaxQueryReadBytes / QueryLifetimeNativeProtocol.GrantDivisor,
            database.Limits.MaxScanRecords / QueryLifetimeNativeProtocol.GrantDivisor,
            work.MaxResultBytes, work.Witness, work.Statistics, work.Scope, work.SourceWindowId, work.Selected);
        var input = new DistributedSearchOwnedLeafV2(original, metadata.Definition, metadata.Coverage, metadata.Principal);
        var bytes = NativeSerialization.Serialize(input);
        var actual = NativeSerialization.Deserialize<DistributedSearchOwnedLeafV2>(bytes);
        await Assert.That(bytes.AsSpan().SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();
        var missing = original with
        {
            Selected = default
        };
        await QueryLifetimeNativeAssertions.RefusedAsync(() => _ = NativeSerialization.Serialize(missing), ErrorCode.Corruption);
        await QueryLifetimeNativeAssertions.RefusedAsync(() => DistributedSearchOwnedLeafValidation.Require(
            missing, database.Limits, options.Value, budget), ErrorCode.Corruption);
        if (phase != DistributedSearchPhase.Projection)
        {
            await Assert.That(actual.Original.Selected.IsEmpty).IsTrue();
            var nonempty = original with
            {
                Selected = [new(RemoteDocumentNativeFixture.Reference, RemoteDocumentNativeFixture.First, SingleTextScore)]
            };
            var invalid = NativeSerialization.Deserialize<DistributedSearchOwnedLeafV1>(NativeSerialization.Serialize(nonempty));
            await QueryLifetimeNativeAssertions.RefusedAsync(() => DistributedSearchOwnedLeafValidation.Require(
                invalid, database.Limits, options.Value, budget), ErrorCode.Corruption);
        }
        return DistributedSearchOwnedLeafExecution.ExecuteV2(database, RemoteDocumentNativeFixture.Reader, actual,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, options, database.EvaluationClock, expiry, CancellationToken.None);
    }
}
