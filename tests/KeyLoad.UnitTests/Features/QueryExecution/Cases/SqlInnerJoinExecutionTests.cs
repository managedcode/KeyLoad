using System.Text.Json;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinExecutionTests
{
    private const string LeftCollection = "orders";
    private const string RightCollection = "customers";
    private const string LeftPrimaryKey = "order_id";
    private const string LeftForeignKey = "customer_id";
    private const string RightPrimaryKey = "id";
    private const string RightName = "name";
    private const string CustomerNameProjectionAlias = "customer_name";
    private const string TotalField = "total";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const string JoinSql = "SELECT l.order_id AS order_id, r.name AS customer_name, l.total AS total "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";
    private const string FirstLeft = "{\"order_id\":\"a\",\"customer_id\":\"c1\",\"total\":1}";
    private const string LastLeft = "{\"order_id\":\"z\",\"customer_id\":\"c2\",\"total\":2}";
    private const string NullLeft = "{\"order_id\":\"m\",\"customer_id\":null,\"total\":3}";
    private const string MissingLeft = "{\"order_id\":\"n\",\"total\":4}";
    private const string UnmatchedLeft = "{\"order_id\":\"x\",\"customer_id\":\"absent\",\"total\":5}";
    private const string EmptyKeyLeft = "{\"order_id\":\"b\",\"customer_id\":\"\",\"total\":6}";
    private const string FirstRight = "{\"id\":\"c1\",\"name\":\"Ada\"}";
    private const string LastRight = "{\"id\":\"c2\",\"name\":\"Lin\"}";
    private const int QueryDialectVersion = 2;
    private const int ExpectedMatches = 2;
    private const int OverlongIdentifierLength = 257;

    [Test]
    public async Task Q2InnerJoinUsesRealZoneTreeRowsAndReturnsOrdinalPairsWithSourceRevisions()
    {
        using var database = new TestDatabase();
        Configure(database);
        var seed = database.Commit(
            new PutDocument(LeftCollection, "z", LastLeft),
            new PutDocument(LeftCollection, "x", UnmatchedLeft),
            new PutDocument(LeftCollection, "m", NullLeft),
            new PutDocument(LeftCollection, "a", FirstLeft),
            new PutDocument(LeftCollection, "b", EmptyKeyLeft),
            new PutDocument(LeftCollection, "n", MissingLeft),
            new PutDocument(LeftCollection, "y", OverlongKeyLeft()),
            new PutDocument(RightCollection, "c2", LastRight),
            new PutDocument(RightCollection, "c1", FirstRight));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        var page = engine.Execute("root", new(database.Partition, JoinSql, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion));

        await Assert.That(page.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(seed.Token.Position);
        await Assert.That(page.Rows.Length).IsEqualTo(ExpectedMatches);
        await Assert.That(page.Rows.Select(row => row.EntityId)).IsEquivalentTo(["a", "z"], CollectionOrdering.Matching);
        await SqlInnerJoinPageAssertions.AssertRowAsync(page.Rows[0], "a", "Ada", 1, "c1");
        await SqlInnerJoinPageAssertions.AssertRowAsync(page.Rows[1], "z", "Lin", 2, "c2");
        var ast = new AstQueryRequest(database.Partition,
            new SelectQuery(LeftCollection, LeftAlias,
                [new("/order_id", "order_id", LeftAlias), new("/name", "customer_name", RightAlias),
                    new("/total", "total", LeftAlias)], null, [new("/order_id", false)], 10,
                InnerJoin: new(RightCollection, RightAlias, "/" + LeftForeignKey, "/" + RightPrimaryKey)),
            AllowFullScan: true, AstVersion: QueryDialectVersion);
        var astPage = engine.ExecuteAst("root", ast);
        await Assert.That(JsonDefaults.Serialize(astPage).AsSpan().SequenceEqual(JsonDefaults.Serialize(page))).IsTrue();
    }

    [Test]
    public async Task Q1AndUnknownDialectOrAstVersionsRejectBeforeReadingAndHealthyQ2StillRuns()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(LeftCollection, "a", FirstLeft), new PutDocument(RightCollection, "c1", FirstRight));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var q1 = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(database.Partition, JoinSql, AllowFullScan: true)));
        var beforeUnsupported = database.Store.Position;
        var badDialect = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(database.Partition, JoinSql, AllowFullScan: true, QueryDialectVersion: 3)));
        var explain = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(database.Partition, "EXPLAIN " + JoinSql, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion)));
        var ast = new AstQueryRequest(database.Partition,
            new SelectQuery(LeftCollection, LeftAlias,
                [new("/order_id", "order_id", LeftAlias), new("/name", "name", RightAlias)], null,
                [new("/order_id", false)], 10, InnerJoin: new(RightCollection, RightAlias, "/" + LeftForeignKey, "/" + RightPrimaryKey)),
            AllowFullScan: true, AstVersion: 3);
        var badAst = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", ast));

        await Assert.That(q1.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(badDialect.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(explain.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(badAst.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(database.Store.Position).IsEqualTo(beforeUnsupported);
        var healthy = engine.Execute("root", new(database.Partition, JoinSql, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion));
        await Assert.That(healthy.Rows).HasSingleItem();
        await Assert.That(healthy.Rows[0].EntityId).IsEqualTo("a");
    }

    [Test]
    public async Task Q1AliasNamedInnerStaysSupportedAndAnInnerJoinRequiresQ2()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(LeftCollection, "a", FirstLeft),
            new PutDocument(RightCollection, "c1", FirstRight));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var q1 = engine.Execute("root", new(database.Partition,
            "SELECT inner.order_id AS order_id FROM orders inner LIMIT 10", AllowFullScan: true));

        await Assert.That(q1.Rows.Select(row => row.EntityId)).IsEquivalentTo(["a"], CollectionOrdering.Matching);
        var beforeUnsupportedJoin = database.Store.Position;
        var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(database.Partition, JoinSql, AllowFullScan: true)));
        await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(database.Store.Position).IsEqualTo(beforeUnsupportedJoin);

        var q2 = engine.Execute("root", new(database.Partition, JoinSql, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion));
        await Assert.That(q2.Rows).HasSingleItem();
        await Assert.That(q2.Rows[0].EntityId).IsEqualTo("a");
    }

    [Test]
    public async Task JoinKeyUseRequiresPersistedGrantsAndProjectedOnlyFieldsRemainSafelyRedacted()
    {
        using var database = new TestDatabase();
        Configure(database,
            [new("/" + LeftForeignKey, "join", RawUseGrant: "left.join", RawReadGrant: "left.read")],
            [new("/" + RightPrimaryKey, "join", RawUseGrant: "right.join", RawReadGrant: "right.read"),
                new("/" + RightName, "private", RawReadGrant: "name.read")]);
        database.Commit(new PutDocument(LeftCollection, "a", FirstLeft), new PutDocument(RightCollection, "c1", FirstRight));
        var reader = new PrincipalRecord("reader", database.Partition.TenantId,
            [new(database.Partition.DatabaseId, LeftCollection, Capability.Query | Capability.DocumentsRead)],
            ["left.join", "left.read", "right.read"]);
        var initialReader = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var beforeScopeDenial = database.Store.Position;
        var resourceDenied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("reader",
            new(database.Partition, JoinSql, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion)));
        await Assert.That(resourceDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.Store.Position).IsEqualTo(beforeScopeDenial);
        var resourceGrantedReader = reader with
        {
            PolicyEpoch = initialReader.PolicyEpoch + 1,
            Grants = [.. reader.Grants, new(database.Partition.DatabaseId, RightCollection, Capability.Query | Capability.DocumentsRead)]
        };
        var persistedResourceGrant = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(resourceGrantedReader)).Get<PrincipalRecord>();
        var beforeFieldDenial = database.Store.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("reader",
            new(database.Partition, JoinSql, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion)));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.Store.Position).IsEqualTo(beforeFieldDenial);
        var fieldAuthorizedReader = resourceGrantedReader with
        {
            PolicyEpoch = persistedResourceGrant.PolicyEpoch + 1,
            FieldGrants = [.. resourceGrantedReader.FieldGrants, "right.join"]
        };
        _ = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(fieldAuthorizedReader)).Get<PrincipalRecord>();

        var page = engine.Execute("reader", new(database.Partition, JoinSql, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion));

        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.Rows[0].Redacted).IsTrue();
        var redactedFields = page.Rows[0].RedactedFields ?? throw new InvalidOperationException();
        await Assert.That(redactedFields).IsEquivalentTo(["r.name"], CollectionOrdering.Matching);
        using var json = JsonDocument.Parse(page.Rows[0].Json);
        await Assert.That(json.RootElement.GetProperty(CustomerNameProjectionAlias).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(page.Rows[0].Json.Contains("Ada", StringComparison.Ordinal)).IsFalse();
        await Assert.That(engine.Execute("root", new(database.Partition, JoinSql, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion)).Rows).HasSingleItem();
    }

    private static void Configure(TestDatabase database, SensitiveFieldPolicy[]? leftPolicies = null,
        SensitiveFieldPolicy[]? rightPolicies = null)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(LeftCollection, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            {
                FieldPolicies = [.. leftPolicies ?? []],
                RelationalSchema = new(LeftPrimaryKey,
                    [new(LeftPrimaryKey, RelationalColumnType.Text), new(LeftForeignKey, RelationalColumnType.Text, Nullable: true),
                        new(TotalField, RelationalColumnType.WholeNumber)])
            }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(RightCollection, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            {
                FieldPolicies = [.. rightPolicies ?? []],
                RelationalSchema = new(RightPrimaryKey,
                    [new(RightPrimaryKey, RelationalColumnType.Text), new(RightName, RelationalColumnType.Text)])
            }));
    }

    private static string OverlongKeyLeft() => JsonSerializer.Serialize(
        new { order_id = "y", customer_id = new string('x', OverlongIdentifierLength), total = 7 }, JsonDefaults.Options);
}
