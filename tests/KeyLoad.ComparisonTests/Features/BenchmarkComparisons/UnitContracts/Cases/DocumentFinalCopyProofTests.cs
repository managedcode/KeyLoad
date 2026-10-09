using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-004: final native RF3 copy proof rejects stale membership and incomplete final durability/readback.</summary>
internal sealed class DocumentFinalCopyProofTests
{
    [Test]
    [Arguments("none", true)]
    [Arguments("flush", false)]
    [Arguments("content", false)]
    [Arguments("membership", false)]
    public async Task AcMeth004OpenSearchRequiresFinalFlushAndEveryActualCopy(string corruption, bool qualified)
    {
        var corpus = new DocumentComparisonCorpus(100, 16);
        var schedule = new DocumentComparisonSchedule(DocumentComparisonScenario.SequentialRead, corpus, 100);
        using var handler = new DocumentFinalOpenSearchHandler(schedule, corruption);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://controlled.invalid") };
        var initial = new TargetProfile("OpenSearch", OpenSearchNames.ExpectedVersion, "controlled", "controlled", "controlled", "HTTP", "controlled", OpenSearchNames.ExpectedImage)
        { Cluster = new(3, 3, "green", [OpenSearchNames.EvidenceNodeIds + "n1,n2,n3"]) };
        ClusterEvidence? evidence = null;
        Exception? failure = null;
        try
        {
            evidence = await OpenSearchDocumentCopyProof.VerifyAsync(client, DocumentFinalOpenSearchHandler.Index, 3,
                ComparisonTopology.Replicated, initial, schedule, UnitBenchmarkOptions.Native(),
                TestContext.Current!.Execution.CancellationToken);
        }
        catch (ComparisonFailureException error) { failure = error; }
        await Assert.That(failure is null).IsEqualTo(qualified);
        await Assert.That(handler.FlushRequests).IsEqualTo(1);
        if (qualified)
        {
            var actual = evidence ?? throw new InvalidOperationException("Final native copy proof is missing.");
            await Assert.That(actual.Nodes).IsEqualTo(3);
            await Assert.That(actual.DataCopies).IsEqualTo(3);
            await Assert.That(handler.ReadCopies.Order(StringComparer.Ordinal)).IsEquivalentTo(new[] { "n1", "n2", "n3" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(actual.Observations.Count(value => value.Contains("full ordered readback", StringComparison.Ordinal))).IsEqualTo(3);
        }
    }

    [Test]
    public async Task AcMeth004RedisAllReplicaFsyncAndStableNativeIdentitiesAreRequired()
    {
        RedisDocumentCopyProof.RequireFsyncReceipt([1, 2], 2);
        await Assert.That(() => RedisDocumentCopyProof.RequireFsyncReceipt([1, 1], 2)).Throws<ComparisonFailureException>();
        await Assert.That(() => RedisDocumentCopyProof.RequireFsyncReceipt([0, 2], 2)).Throws<ComparisonFailureException>();
        var initial = new ClusterEvidence(3, 3, "controlled", [RedisNodeIdentity.ObservationPrefix + "primary run_id=a", RedisNodeIdentity.ObservationPrefix + "replica-1 run_id=b", RedisNodeIdentity.ObservationPrefix + "replica-2 run_id=c"]);
        RedisDocumentCopyProof.RequireInitialIdentities(initial, initial);
        var changed = initial with { Observations = initial.Observations.SetItem(2, RedisNodeIdentity.ObservationPrefix + "replica-2 run_id=d") };
        await Assert.That(() => RedisDocumentCopyProof.RequireInitialIdentities(initial, changed)).Throws<ComparisonFailureException>();
    }

    [Test]
    public async Task AcMeth004PostgresRequiresFreshFinalWalAndUnchangedNamedStandbys()
    {
        var schedule = new DocumentComparisonSchedule(DocumentComparisonScenario.Delete, new DocumentComparisonCorpus(100, 16), 100);
        var initial = new TargetProfile("PostgreSQL + pgvector", "controlled", "controlled", "controlled", "controlled", "TCP", "controlled", "controlled")
        { Cluster = new(3, 3, "controlled", ["Native standby identities=benchmark_standby1@one,benchmark_standby2@two"]) };
        var observed = initial with { Cluster = initial.Cluster! with { Observations = initial.Cluster!.Observations.Add("Named standbys flushed and replayed seeded corpus through WAL 0/123") } };
        var final = PostgresDocumentCopyProof.BindFinalState(observed, initial, schedule);
        await Assert.That(final.Cluster!.Observations.Any(value => value.Contains("final document state through WAL 0/123", StringComparison.Ordinal))).IsTrue();
        await Assert.That(final.Cluster!.Observations.Any(value => value.Contains("records=0", StringComparison.Ordinal))).IsTrue();
        await Assert.That(() => PostgresDocumentCopyProof.BindFinalState(initial, initial, schedule)).Throws<ComparisonFailureException>();
        var changed = observed with { Cluster = observed.Cluster! with { Observations = observed.Cluster!.Observations.SetItem(0, "Native standby identities=foreign") } };
        await Assert.That(() => PostgresDocumentCopyProof.BindFinalState(changed, initial, schedule)).Throws<ComparisonFailureException>();
    }
}
