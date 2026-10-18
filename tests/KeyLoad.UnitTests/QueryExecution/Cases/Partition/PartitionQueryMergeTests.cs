using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryMergeTests
{
    private const string LabelProperty = "label";
    private const string RestrictedPath = "/secret";
    private const string RestrictedAlias = "secret";
    private const string RestrictedProjectionJson = "{\"secret\":null}";
    [Test]
    public async Task RealZoneTreeLeavesMatchIndependentGlobalOrderBeforeProjection()
    {
        using var database = PartitionQueryTestSupport.Create(out var creation);
        var partitions = PartitionQueryTestSupport.Partitions(database);
        PartitionQuerySeed[][] seeds =
        [
            [new("same", 10, "alpha \"Ω\\n"), new("z", 8, "a-z"), new("a", 8, "a-a"), new("low", 1, "a-low"), new("tail-a", -1, "tail-a"), new("tail-b", -2, "tail-b")],
            [new("same", 10, "beta"), new("b", 9, "b-b"), new("c", 8, "b-c"), new("low", 0, "b-low"), new("tail-c", -3, "tail-c"), new("tail-d", -4, "tail-d")],
            [new("same", 10, "gamma"), new("d", 9, "c-d"), new("x", 7, "c-x"), new("low", -1, "c-low"), new("tail-e", -5, "tail-e"), new("tail-f", -6, "tail-f")]
        ];
        for (var index = 0; index < partitions.Length; index++)
        {
            PartitionQueryTestSupport.AddRows(database, partitions[index], seeds[index]);
        }

        var actual = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database), [.. partitions]);
        var expected = seeds.SelectMany((items, index) => items.Select(item => new
        {
            Partition = partitions[index],
            Seed = item
        }))
            .OrderByDescending(item => item.Seed.Score)
            .ThenBy(item => item.Partition.TenantId, StringComparer.Ordinal)
            .ThenBy(item => item.Partition.DatabaseId, StringComparer.Ordinal)
            .ThenBy(item => item.Partition.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(item => item.Partition.PartitionKey, StringComparer.Ordinal)
            .ThenBy(item => PartitionQueryTestSupport.Collection, StringComparer.Ordinal)
            .ThenBy(item => item.Seed.Id, StringComparer.Ordinal)
            .Take(PartitionQueryTestSupport.ResultLimit).ToArray();

        var actualReferences = actual.Rows.Select(row => row.Reference).ToArray();
        var expectedReferences = expected.Select(item => PartitionQueryCreationOracle.Reference(creation, item.Partition, item.Seed.Id)).ToArray();
        await Assert.That(actual.Complete).IsTrue();
        await Assert.That(actualReferences.SequenceEqual(expectedReferences)).IsTrue();
        for (var index = 0; index < expected.Length; index++)
        {
            using var json = JsonDocument.Parse(actual.Rows[index].Row.Json);
            await Assert.That(json.RootElement.GetProperty(LabelProperty).GetString())
                .IsEqualTo(expected[index].Seed.Label);
            await Assert.That(actual.Rows[index].Row.Revision).IsEqualTo(1L);
        }
        await Assert.That(actual.Leaves.Length).IsEqualTo(partitions.Length);
        await Assert.That(actual.Leaves.All(leaf => leaf.Candidates.Length == PartitionQueryTestSupport.ResultLimit)).IsTrue();
        await Assert.That(actual.Leaves.All(leaf => leaf.NodeId == database.Store.Identity.NodeId
            && leaf.Incarnation == database.Store.Identity.Incarnation
            && leaf.ReadGeneration == database.Store.Identity.ReadGeneration
            && leaf.CutPosition > 0)).IsTrue();
        await Assert.That(actual.Leaves.Select(leaf => leaf.PolicyEpoch).Distinct().Count()).IsEqualTo(1);
        await Assert.That(actual.RetainedBytes).IsEqualTo(PartitionQueryReferenceSizing.RetainedBytes(actual));
    }

    [Test]
    public async Task FullReferenceTiesUseOrdinalUnicodePartitionOrder()
    {
        using var database = PartitionQueryTestSupport.Create(out var creation);
        PartitionRef[] partitions =
        [
            database.Partition with { PartitionKey = "unicode-\uE000" },
            database.Partition with { PartitionKey = "unicode-\U00010000" }
        ];
        PartitionQueryTestSupport.AddRows(database, partitions[0], new PartitionQuerySeed("same", 7, "private-use"));
        PartitionQueryTestSupport.AddRows(database, partitions[1], new PartitionQuerySeed("same", 7, "supplementary"));

        var result = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database, 2), [.. partitions]);
        var expected = partitions.OrderBy(static item => item.PartitionKey, StringComparer.Ordinal)
            .Select(item => PartitionQueryCreationOracle.Reference(creation, item, "same")).ToArray();

        await Assert.That(result.Rows.Select(static row => row.Reference).SequenceEqual(expected)).IsTrue();
        await Assert.That(result.Rows.Select(static row => row.Row.Json)
            .SequenceEqual(["{\"label\":\"supplementary\"}", "{\"label\":\"private-use\"}"])).IsTrue();
    }

    [Test]
    public async Task IndependentRetainedOracleIncludesPersistedRedactionMetadata()
    {
        using var database = PartitionQueryTestSupport.Create(fields: [new(RestrictedPath, "private")]);
        PartitionQueryTestSupport.AddRows(database, database.Partition,
            new PartitionQuerySeed("redacted", 9, "visible", "NEVER-RETURN"));
        PartitionQueryTestSupport.ConfigureSinglePartitionReader(database);

        var position = database.Store.Position;
        var identity = database.Store.Identity;
        var request = PartitionQueryTestSupport.Request(database, 1);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var result = engine.ExecutePartitionQuery(PartitionQueryTestSupport.Reader, request, [database.Partition]);
        var row = result.Rows.Single().Row;

        await Assert.That(row.Json).IsEqualTo("{\"label\":\"visible\"}");
        await Assert.That(row.Redacted).IsFalse();
        await Assert.That(row.RedactedFields!.Value).IsEmpty();
        await Assert.That(result.RetainedBytes).IsEqualTo(PartitionQueryReferenceSizing.RetainedBytes(result));
        var selectedRequest = request with
        {
            Query = request.Query with
            {
                Projection = [new(RestrictedPath, RestrictedAlias)]
            }
        };
        var selected = engine.ExecutePartitionQuery(PartitionQueryTestSupport.Reader, selectedRequest, [database.Partition]);
        var selectedRow = selected.Rows.Single();
        await Assert.That(selected.Complete).IsTrue();
        await Assert.That(selectedRow.Reference).IsEqualTo(result.Rows.Single().Reference);
        await Assert.That(selectedRow.Row.Revision).IsEqualTo(row.Revision);
        await Assert.That(selectedRow.Row.Json).IsEqualTo(RestrictedProjectionJson);
        await Assert.That(selectedRow.Row.Redacted).IsTrue();
        await Assert.That(selectedRow.Row.RedactedFields!.Value.SequenceEqual([RestrictedPath])).IsTrue();
        var leaf = selected.Leaves.Single();
        await Assert.That(leaf.Partition).IsEqualTo(database.Partition);
        await Assert.That(leaf.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(leaf.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(leaf.ReadGeneration).IsEqualTo(identity.ReadGeneration);
        await Assert.That(leaf.CutPosition).IsEqualTo(result.Leaves.Single().CutPosition);
        await Assert.That(selected.RetainedBytes).IsEqualTo(PartitionQueryReferenceSizing.RetainedBytes(selected));
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(database.Store.Identity).IsEqualTo(identity);
    }

    [Test]
    public async Task InvalidLeafSetIsRejectedAndAHealthyQueryStillRuns()
    {
        using var database = PartitionQueryTestSupport.Create();
        var partitions = PartitionQueryTestSupport.Partitions(database);
        PartitionQueryTestSupport.AddRows(database, partitions[0], new PartitionQuerySeed("one", 1, "one"));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecutePartitionQuery(
            PartitionQueryTestSupport.Principal, PartitionQueryTestSupport.Request(database),
            [database.Partition, database.Partition]));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = engine.ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database), [database.Partition]);
        await Assert.That(healthy.Rows.Single().Reference.Id).IsEqualTo("one");
    }
}
