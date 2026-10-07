using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class Q2CapabilityOperation
{
    internal static async Task VerifyAsync(TestDatabase database, QueryEngine engine, AstQueryRequest request)
    {
        var position = database.Store.Position;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var expected = engine.Execute("root", new(database.Partition, SqlInnerJoinRejectionFixture.HealthySql,
            AllowFullScan: true, QueryDialectVersion: 2));
        var actual = engine.ExecuteAst("root", request);
        await Assert.That(actual.AccessPath).IsEqualTo("bounded-primary-key-inner-join");
        await Assert.That(actual.Rows).HasSingleItem();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.CutPosition).IsEqualTo(position);
        await SqlInnerJoinPageAssertions.AssertRowAsync(actual.Rows.Single(), "order", "Ada", 7, "customer");
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    internal static async Task RejectAsync(TestDatabase database, QueryEngine engine, AstQueryRequest request)
    {
        var position = database.Store.Position;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var native = database.Store.GetReadDiagnostics();
        QueryPage? result = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result = engine.ExecuteAst("root", request));
        await Assert.That(database.Store.GetReadDiagnostics()).IsEqualTo(native);
        await Assert.That(result).IsNull();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(failure.Message).IsEqualTo("The query AST version is unsupported.");
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }
}
