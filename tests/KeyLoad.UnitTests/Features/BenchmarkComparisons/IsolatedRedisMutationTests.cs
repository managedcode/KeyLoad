using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedRedisMutationTests
{
    private const string PrimaryIdentity = "primary";
    private const string FirstReplica = "replica-one";
    private const string SecondReplica = "replica-two";
    private const string Version = "8.4.0";
    private const string State = "native replicas";

    [Test]
    public void AcIso005NativeRedisCreateUpdateDeleteMustAffectOneExistingOrAbsentKey()
    {
        RedisMutationContract.RequireSet(true, Scenario.DocumentWrite);
        RedisMutationContract.RequireSet(true, Scenario.DocumentUpdate);
        RedisMutationContract.RequireDelete(true);
        Assert.ThrowsExactly<ComparisonFailureException>(() => RedisMutationContract.RequireSet(false, Scenario.DocumentWrite));
        Assert.ThrowsExactly<ComparisonFailureException>(() => RedisMutationContract.RequireSet(false, Scenario.DocumentUpdate));
        Assert.ThrowsExactly<ComparisonFailureException>(() => RedisMutationContract.RequireDelete(false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RedisMutationContract.RequireSet(true, Scenario.PointRead));
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AcIso003RedisEvidenceRecordsActualDirectReplicaCount(int replicas)
    {
        var primary = new RedisNodeIdentity(PrimaryIdentity, Version);
        var peers = new[] { new RedisNodeIdentity(FirstReplica, Version), new RedisNodeIdentity(SecondReplica, Version) };
        var selected = peers.Take(replicas).ToArray();
        RedisNodeIdentity.RequireUniqueVersionedSet(selected.Prepend(primary).ToArray(), primary, replicas + 1);
        var evidence = RedisNodeIdentity.ReplicatedEvidence(primary, selected, State);
        await Assert.That(evidence.Nodes).IsEqualTo(replicas + 1);
        await Assert.That(evidence.DataCopies).IsEqualTo(replicas + 1);
        await Assert.That(evidence.Observations.Any(value => value.Contains("two connected replicas", StringComparison.Ordinal)))
            .IsEqualTo(replicas == 2);
        Assert.ThrowsExactly<ComparisonFailureException>(() =>
            RedisNodeIdentity.RequireUniqueVersionedSet(selected.Prepend(primary).ToArray(), primary, replicas + 2));
    }
}
