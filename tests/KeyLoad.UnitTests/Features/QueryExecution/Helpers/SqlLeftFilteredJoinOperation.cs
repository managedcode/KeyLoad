using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlLeftFilteredJoinOperation
{
    private const string MinimumParameter = "minimum";

    internal static async Task VerifyAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var position = database.Store.Position;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var request = SqlLeftFilteredJoinFixture.Request(database.Partition);
        var sql = engine.Execute("root", request);
        var ast = engine.ExecuteAst("root", SqlLeftFilteredJoinFixture.Ast(database.Partition));
        await SqlLeftFilteredJoinAssertions.PageAsync(sql, position);
        await Assert.That(JsonDefaults.Serialize(ast)).IsEquivalentTo(JsonDefaults.Serialize(sql), CollectionOrdering.Matching);
        await SqlLeftFilteredJoinAssertions.PageAsync(engine.Execute("root",
            request with
            {
                Sql = SqlLeftFilteredJoinFixture.LiteralSql,
                Parameters = null
            }), position);
        var combination = request with
        {
            Sql = SqlLeftFilteredJoinFixture.Prefix
            + "WHERE (l.total IN (7, @minimum) AND l.total BETWEEN 7 AND 8) "
            + SqlLeftFilteredJoinFixture.Order
        };
        await SqlLeftFilteredJoinAssertions.PageAsync(engine.Execute("root", combination), position);
        var original = SqlLeftFilteredJoinFixture.Ast(database.Partition);
        var operand = new FieldOperand("/total");
        var missingCombination = original with
        {
            Query = original.Query with
            {
                Filter = new Logical(new Logical(
                    new InPredicate(operand, [ValueOperand.Create(7), new ParameterOperand(MinimumParameter)], false), "AND",
                    new Logical(new Comparison(operand, ">=", ValueOperand.Create(7)), "AND",
                        new Comparison(operand, "<=", ValueOperand.Create(8)))), "OR", new NullTest(operand, false, true))
            }
        };
        await SqlLeftFilteredJoinAssertions.PageAsync(engine.ExecuteAst("root", missingCombination), position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task RefusalsAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlLeftFilteredJoinFixture.Request(database.Partition);
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Parameters = null }), ErrorCode.Validation);
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with
            {
                Parameters = new()
                {
                    [MinimumParameter] = JsonSerializer.SerializeToElement("wrong")
                }
            }), ErrorCode.Validation);
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with
            {
                Sql = SqlLeftFilteredJoinFixture.Prefix
                + "WHERE r.name = @minimum " + SqlLeftFilteredJoinFixture.Order
            }), ErrorCode.UnsupportedCapability);
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with
            {
                Sql = SqlLeftFilteredJoinFixture.Prefix
                + "WHERE l.total.nested = @minimum " + SqlLeftFilteredJoinFixture.Order
            }), ErrorCode.UnsupportedCapability);
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            engine.Execute("root", request, cancellationToken: cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await SqlLeftFilteredJoinAssertions.PageAsync(engine.Execute("root", request), position);
    }

    internal static async Task PolicyAsync(TestDatabase database)
    {
        var principal = new PrincipalRecord(SqlLeftFilteredJoinFixture.Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, SqlLeftFilteredJoinFixture.Left, Capability.Query | Capability.DocumentsRead),
                new(database.Partition.DatabaseId, SqlLeftFilteredJoinFixture.Right, Capability.Query | Capability.DocumentsRead)], [SqlLeftFilteredJoinFixture.FilterReadGrant]);
        var persisted = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlLeftFilteredJoinFixture.Request(database.Partition);
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute(principal.Id, request), ErrorCode.PermissionDenied);
        var repaired = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal with
            {
                PolicyEpoch = persisted.PolicyEpoch + 1,
                FieldGrants = [SqlLeftFilteredJoinFixture.UseGrant, SqlLeftFilteredJoinFixture.FilterReadGrant]
            }))
            .Get<PrincipalRecord>();
        await SqlLeftFilteredJoinAssertions.PageAsync(engine.Execute(principal.Id, request), database.Store.Position, masked: true);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(repaired with
        {
            PolicyEpoch = repaired.PolicyEpoch + 1,
            FieldGrants = [SqlLeftFilteredJoinFixture.FilterReadGrant]
        }));
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute(principal.Id, request), ErrorCode.PermissionDenied);
        await VerifyAsync(database);
    }
}
