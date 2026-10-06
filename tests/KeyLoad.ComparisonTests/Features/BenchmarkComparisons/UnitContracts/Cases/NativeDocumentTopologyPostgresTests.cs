using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/005 native SQL, member and affected-row contracts without a database double.</summary>
internal sealed class NativeDocumentTopologyPostgresTests
{
    private const string TwoNodeQuorum = "ANY 1 (\"benchmark_standby1\")";
    private const string ThreeNodeQuorum = "ANY 1 (\"benchmark_standby1\", \"benchmark_standby2\")";
    private const string StandbyOne = "benchmark_standby1";
    private const string StandbyTwo = "benchmark_standby2";
    private const string AddressOne = "10.0.0.2";
    private const string AddressTwo = "10.0.0.3";
    private const string DocumentId = "strict-document";
    private const string Json = "{\"value\":2}";
    private const string InsertSql = "INSERT INTO documents(id,body) VALUES ($1,$2)";
    private const string UpdateSql = "UPDATE documents SET body=$2 WHERE id=$1";
    private const string DeleteSql = "DELETE FROM documents WHERE id=$1";

    [Test]
    public async Task AC_ISO_003_RequiresExactDistinctCountDerivedStandbysAndQuorum()
    {
        await Assert.That(PostgresTopology.QuorumSettings(ComparisonTopology.TwoNode)).IsEqualTo(TwoNodeQuorum);
        await Assert.That(PostgresTopology.QuorumSettings(ComparisonTopology.Replicated)).IsEqualTo(ThreeNodeQuorum);
        await Assert.That(PostgresTopology.HasExpectedMembers(ComparisonTopology.TwoNode, [(StandbyOne, AddressOne)])).IsTrue();
        await Assert.That(PostgresTopology.HasExpectedMembers(ComparisonTopology.TwoNode,
            [(StandbyOne, AddressOne), (StandbyTwo, AddressTwo)])).IsFalse();
        await Assert.That(PostgresTopology.HasExpectedMembers(ComparisonTopology.Replicated,
            [(StandbyOne, AddressOne), (StandbyTwo, AddressOne)])).IsFalse();
        await Assert.That(PostgresTopology.HasExpectedMembers(ComparisonTopology.Replicated,
            [(StandbyOne, AddressOne), (StandbyTwo, AddressTwo)])).IsTrue();
    }

    [Test]
    public async Task AC_ISO_005_UsesNativeStrictCreateUpdateDeleteCommandsAndTypedBodies()
    {
        using var connection = new NpgsqlConnection();
        var input = new BenchmarkDocument(0, DocumentId, Json, []);
        using var create = PostgresDocumentOperations.CreateCommand(connection, Scenario.DocumentWrite, input);
        using var update = PostgresDocumentOperations.CreateCommand(connection, Scenario.DocumentUpdate, input);
        using var delete = PostgresDocumentOperations.CreateCommand(connection, Scenario.DocumentDelete, input);
        await Assert.That(create.CommandText).IsEqualTo(InsertSql);
        await Assert.That(update.CommandText).IsEqualTo(UpdateSql);
        await Assert.That(delete.CommandText).IsEqualTo(DeleteSql);
        await Assert.That(create.Parameters[0].NpgsqlDbType).IsEqualTo(NpgsqlDbType.Text);
        await Assert.That(update.Parameters[1].NpgsqlDbType).IsEqualTo(NpgsqlDbType.Jsonb);
        await Assert.That(update.Parameters[0].Value).IsEqualTo(DocumentId);
        await Assert.That(update.Parameters[1].Value).IsEqualTo(Json);
        await Assert.That(delete.Parameters.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AC_ISO_005_OnlyExactNativeMissingCountsBecomeExpectedNegativeOutcomes()
    {
        PostgresDocumentOperations.RequireAffected(Scenario.DocumentUpdate, 1);
        PostgresDocumentOperations.RequireAffected(Scenario.DocumentDelete, 1);
        var update = Assert.ThrowsExactly<ComparisonFailureException>(() => PostgresDocumentOperations.RequireAffected(Scenario.DocumentUpdate, 0));
        var delete = Assert.ThrowsExactly<ComparisonFailureException>(() => PostgresDocumentOperations.RequireAffected(Scenario.DocumentDelete, 0));
        var excess = Assert.ThrowsExactly<ComparisonFailureException>(() => PostgresDocumentOperations.RequireAffected(Scenario.DocumentUpdate, 2));
        await Assert.That(update.Message).IsEqualTo(ComparisonMutationFailures.UpdateMissing);
        await Assert.That(delete.Message).IsEqualTo(ComparisonMutationFailures.DeleteMissing);
        await Assert.That(excess.Message).IsEqualTo(ComparisonMutationFailures.CardinalityMismatch);
        Assert.ThrowsExactly<ComparisonFailureException>(() => PostgresDocumentOperations.RequireAffected(Scenario.DocumentWrite, 0));
        Assert.ThrowsExactly<ComparisonFailureException>(() => PostgresDocumentOperations.RequireAffected(Scenario.DocumentDelete, -1));
    }
}
