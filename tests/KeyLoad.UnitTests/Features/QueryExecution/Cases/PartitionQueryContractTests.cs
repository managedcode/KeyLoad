using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryContractTests
{
    [Test]
    public async Task GeneratedPlanAndCompleteNativeResultRoundTripWithStableFields()
    {
        using var database = PartitionQueryTestSupport.Create();
        var partitions = PartitionQueryTestSupport.Partitions(database)[..2];
        PartitionQueryTestSupport.AddRows(database, partitions[0], new PartitionQuerySeed("same", 5, "first"));
        PartitionQueryTestSupport.AddRows(database, partitions[1], new PartitionQuerySeed("same", 4, "second"));
        var limits = database.Database.Limits;
        var request = QueryValidation.Normalize(PartitionQueryTestSupport.Request(database, 2), limits, UnitExecutionOptions.QueryExecution().Value);
        var plan = PartitionQueryPlanFactory.Create(request, database.Store.Identity, [.. partitions], limits, UnitExecutionOptions.QueryExecution().Value);
        var planRoundTrip = NativeSerialization.Deserialize<PartitionQueryPlanV1>(NativeSerialization.Serialize(plan));
        var result = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database, 2), [.. partitions]);
        var resultRoundTrip = NativeSerialization.Deserialize<PartitionQueryResultV1>(NativeSerialization.Serialize(result));

        await Assert.That(JsonDefaults.Serialize(planRoundTrip).AsSpan().SequenceEqual(JsonDefaults.Serialize(plan))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(resultRoundTrip).AsSpan().SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        await Assert.That(resultRoundTrip.Rows.Select(row => row.Reference.Id).SequenceEqual(["same", "same"])).IsTrue();
        await Assert.That(resultRoundTrip.Rows[0].Reference.Partition).IsNotEqualTo(resultRoundTrip.Rows[1].Reference.Partition);
    }

    [Test]
    public async Task MismatchedLeafRequestPartitionFailsBeforeAnyStorageRead()
    {
        using var database = PartitionQueryTestSupport.Create();
        var partitions = PartitionQueryTestSupport.Partitions(database)[..2];
        PartitionQueryTestSupport.AddRows(database, partitions[0], new PartitionQuerySeed("valid-leaf", 1, "healthy"));
        var limits = database.Database.Limits;
        var request = QueryValidation.Normalize(PartitionQueryTestSupport.Request(database, 1), limits, UnitExecutionOptions.QueryExecution().Value);
        var plan = PartitionQueryPlanFactory.Create(request, database.Store.Identity, [.. partitions], limits, UnitExecutionOptions.QueryExecution().Value);
        var original = plan.Leaves[1];
        var invalid = plan with
        {
            Leaves = plan.Leaves.SetItem(1,
            original with { Request = original.Request with { Partition = database.Partition } })
        };
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => PartitionQueryPlanValidation.Validate(invalid, limits, UnitExecutionOptions.QueryExecution().Value));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(
            PartitionQueryTestSupport.Principal, PartitionQueryTestSupport.Request(database, 1), [partitions[0]]);
        await PartitionQueryWholeFlowAssertions.AssertNativeRowAsync(healthy, partitions[0], "valid-leaf", "healthy");
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task EmptyOversizedAndNullPartitionSetsFailAsValidation()
    {
        using var database = PartitionQueryTestSupport.Create();
        PartitionQueryTestSupport.AddRows(database, database.Partition, new PartitionQuerySeed("valid-leaf", 1, "healthy"));
        var request = QueryValidation.Normalize(PartitionQueryTestSupport.Request(database, 1),
            database.Database.Limits, UnitExecutionOptions.QueryExecution().Value);
        var position = database.Store.Position;
        var oversized = Enumerable.Range(0, 9).Select(index => database.Partition with
        { PartitionKey = "leaf-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray();
        var emptyFailure = Assert.ThrowsExactly<KeyLoadException>(() => PartitionQueryPlanFactory.Create(
            request, database.Store.Identity, [], database.Database.Limits, UnitExecutionOptions.QueryExecution().Value));
        var oversizedFailure = Assert.ThrowsExactly<KeyLoadException>(() => PartitionQueryPlanFactory.Create(
            request, database.Store.Identity, [.. oversized], database.Database.Limits, UnitExecutionOptions.QueryExecution().Value));
        var nullFailure = Assert.ThrowsExactly<KeyLoadException>(() => PartitionQueryPlanFactory.Create(
            request, database.Store.Identity, [database.Partition, null!], database.Database.Limits, UnitExecutionOptions.QueryExecution().Value));

        await Assert.That(emptyFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(oversizedFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(nullFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(
            PartitionQueryTestSupport.Principal, PartitionQueryTestSupport.Request(database, 1), [database.Partition]);
        await PartitionQueryWholeFlowAssertions.AssertNativeRowAsync(healthy, database.Partition, "valid-leaf", "healthy");
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
