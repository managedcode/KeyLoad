using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class ImmutableQueryInputTests
{
    private const string Collection = "orders";
    private const string Principal = "root";

    [Test]
    public async Task AcRoc003_DefaultRequiredAstCollectionsRejectBeforeAStoreScan()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var before = db.Store.GetReadDiagnostics();
        var missingProjection = new AstQueryRequest(db.Partition,
            new SelectQuery(Collection, null, default, null, [], 1), AllowFullScan: true);
        var missingOrder = new AstQueryRequest(db.Partition,
            new SelectQuery(Collection, null, [new("*", "document")], null, default, 1), AllowFullScan: true);
        var invalidIn = new AstQueryRequest(db.Partition,
            new SelectQuery(Collection, null, [new("*", "document")],
                new InPredicate(new FieldOperand("/@id"), default(ImmutableArray<Operand>), false), [], 1),
            AllowFullScan: true);
        var first = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Principal, missingProjection));
        var second = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Principal, missingOrder));
        var third = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Principal, invalidIn));
        var after = db.Store.GetReadDiagnostics();
        await Assert.That(first.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(second.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(third.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(engine.ExecuteAst(Principal, new(db.Partition,
            new SelectQuery(Collection, null, [new("*", "document")], null, [], 1), AllowFullScan: true)).Rows).IsEmpty();
    }
}
