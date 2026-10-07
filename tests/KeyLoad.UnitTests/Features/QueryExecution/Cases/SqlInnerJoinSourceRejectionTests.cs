using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinSourceRejectionTests
{
    [Test]
    [Arguments("non-primary-key", ErrorCode.Validation)]
    [Arguments("numeric-left-key", ErrorCode.Validation)]
    [Arguments("numeric-right-key", ErrorCode.Validation)]
    [Arguments("missing-left-key", ErrorCode.Validation)]
    [Arguments("missing-right-key", ErrorCode.Validation)]
    [Arguments("nested-left-key", ErrorCode.Validation)]
    [Arguments("undeclared-projection", ErrorCode.Validation)]
    [Arguments("nested-projection", ErrorCode.Validation)]
    [Arguments("unknown-projection-source", ErrorCode.Validation)]
    [Arguments("untyped-right", ErrorCode.Validation)]
    [Arguments("untyped-left", ErrorCode.Validation)]
    [Arguments("wrong-kind-right", ErrorCode.Validation)]
    [Arguments("wrong-kind-left", ErrorCode.Validation)]
    [Arguments("missing-right", ErrorCode.NotFound)]
    [Arguments("cross-domain-right", ErrorCode.Conflict)]
    [Arguments("cross-domain-left", ErrorCode.Conflict)]
    [Arguments("ast-one", ErrorCode.UnsupportedCapability)]
    [Arguments("ast-unknown", ErrorCode.UnsupportedCapability)]
    [Arguments("missing-left-alias", ErrorCode.UnsupportedCapability)]
    [Arguments("missing-projection-source", ErrorCode.UnsupportedCapability)]
    [Arguments("same-alias", ErrorCode.UnsupportedCapability)]
    [Arguments("same-collection", ErrorCode.UnsupportedCapability)]
    [Arguments("cursor", ErrorCode.UnsupportedCapability)]
    [Arguments("no-full-scan", ErrorCode.UnsupportedCapability)]
    public async Task PersistedSourceAndAstContractRejectsWithoutNativeScanAndHealthySameEngineRead(string scenario, ErrorCode code)
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = JsonDefaults.Serialize(engine.Execute(SqlInnerJoinRejectionFixture.Root,
            new(database.Partition, SqlInnerJoinRejectionFixture.HealthySql, AllowFullScan: true, QueryDialectVersion: 2)));
        var request = InvalidRequest(SqlInnerJoinRejectionFixture.Ast(database), scenario);
        var position = database.Store.Position;
        var diagnostics = database.Store.GetReadDiagnostics();

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlInnerJoinRejectionFixture.Root, request));

        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsEqualTo(diagnostics.RangeExaminedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await SqlInnerJoinRejectionFixture.VerifyHealthyAsync(database, engine, before, position);
    }

    private static AstQueryRequest InvalidRequest(AstQueryRequest request, string scenario)
    {
        var query = request.Query;
        var join = query.InnerJoin!;
        return scenario switch
        {
            "non-primary-key" => request with { Query = query with { InnerJoin = join with { RightKeyPath = "/alternate" } } },
            "numeric-left-key" => request with { Query = query with { InnerJoin = join with { LeftKeyPath = "/quantity" } } },
            "numeric-right-key" => request with { Query = query with { InnerJoin = join with { RightKeyPath = "/quantity" } } },
            "missing-left-key" => request with { Query = query with { InnerJoin = join with { LeftKeyPath = "/absent" } } },
            "missing-right-key" => request with { Query = query with { InnerJoin = join with { RightKeyPath = "/absent" } } },
            "nested-left-key" => request with { Query = query with { InnerJoin = join with { LeftKeyPath = "/customer_id/nested" } } },
            "undeclared-projection" => request with { Query = query with { Projection = [new("/absent", "value", "l")] } },
            "nested-projection" => request with { Query = query with { Projection = [new("/order_id/nested", "value", "l")] } },
            "unknown-projection-source" => request with { Query = query with { Projection = [new("/order_id", "value", "s")] } },
            "untyped-right" => request with { Query = query with { InnerJoin = join with { Collection = "untyped" } } },
            "untyped-left" => request with { Query = query with { Collection = "untyped" } },
            "wrong-kind-right" => request with { Query = query with { InnerJoin = join with { Collection = "queue" } } },
            "wrong-kind-left" => request with { Query = query with { Collection = "queue" } },
            "missing-right" => request with { Query = query with { InnerJoin = join with { Collection = "absent" } } },
            "cross-domain-right" => request with { Query = query with { InnerJoin = join with { Collection = "foreign" } } },
            "cross-domain-left" => request with { Query = query with { Collection = "foreign" } },
            "ast-one" => request with { AstVersion = 1 },
            "ast-unknown" => request with { AstVersion = 3 },
            "missing-left-alias" => request with { Query = query with { Alias = null } },
            "missing-projection-source" => request with { Query = query with { Projection = [new("/order_id", "value")] } },
            "same-alias" => request with { Query = query with { InnerJoin = join with { Alias = "l" } } },
            "same-collection" => request with { Query = query with { InnerJoin = join with { Collection = "orders" } } },
            "cursor" => request with { Cursor = "unsupported-cursor" },
            "no-full-scan" => request with { AllowFullScan = false },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }
}
