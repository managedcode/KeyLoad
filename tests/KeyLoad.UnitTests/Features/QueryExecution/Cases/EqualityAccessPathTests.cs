using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class EqualityAccessPathTests
{
    private const string ContradictoryIndex = "SELECT * FROM orders WHERE status = 'open' AND status = 'closed'";
    private const string ReversedContradictoryIndex = "SELECT * FROM orders WHERE 'open' = status AND 'closed' = status";
    private const string NullResidualIndex = "SELECT * FROM orders WHERE status = 'open' AND n = NULL";
    private const string ReversedNullResidualIndex = "SELECT * FROM orders WHERE 'open' = status AND NULL = n";
    private const string ContradictoryPoint = "SELECT * FROM orders WHERE id = 'a' AND id = 'b'";
    private const string ReversedContradictoryPoint = "SELECT * FROM orders WHERE 'a' = id AND 'b' = id";
    private const string NullResidualPoint = "SELECT * FROM orders WHERE id = 'a' AND n = NULL";
    private const string ReversedNullResidualPoint = "SELECT * FROM orders WHERE 'a' = id AND NULL = n";
    private const string NullIndex = "SELECT * FROM orders WHERE status = NULL";
    private const string ReversedNullIndex = "SELECT * FROM orders WHERE NULL = status";
    private const string Disjunction = "SELECT * FROM orders WHERE 'a' = id OR 'b' = id ORDER BY id";

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AcAisql008ReversedLiteralAndParameterEqualityUsesTheSameNativePathAndOrderedRows(bool indexed, bool parameterized)
    {
        using var db = EqualityAccessPathSupport.Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var ordinary = engine.Execute(EqualityAccessPathSupport.Root,
            EqualityAccessPathSupport.Request(db, indexed, parameterized, reversed: false));
        var reversed = engine.Execute(EqualityAccessPathSupport.Root,
            EqualityAccessPathSupport.Request(db, indexed, parameterized, reversed: true));

        await Assert.That(ordinary.AccessPath).IsEqualTo(indexed ? EqualityAccessPathSupport.IndexPath : EqualityAccessPathSupport.PointPath);
        await Assert.That(string.Join(EqualityAccessPathSupport.Separator, ordinary.Rows.Select(row => row.EntityId)))
            .IsEqualTo(indexed ? EqualityAccessPathSupport.IndexedIds : EqualityAccessPathSupport.PrimaryId);
        await EqualityAccessPathSupport.SameRows(ordinary, reversed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AcAisql008NativeEqualityCandidatesStillEvaluateContradictoryAndUnknownResiduals(bool indexed, bool nullResidual)
    {
        using var db = EqualityAccessPathSupport.Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var queries = (indexed, nullResidual) switch
        {
            (false, false) => (Ordinary: ContradictoryPoint, Reversed: ReversedContradictoryPoint),
            (false, true) => (Ordinary: NullResidualPoint, Reversed: ReversedNullResidualPoint),
            (true, false) => (Ordinary: ContradictoryIndex, Reversed: ReversedContradictoryIndex),
            (true, true) => (Ordinary: NullResidualIndex, Reversed: ReversedNullResidualIndex)
        };
        var ordinary = engine.Execute(EqualityAccessPathSupport.Root, new(db.Partition, queries.Ordinary));
        var reversed = engine.Execute(EqualityAccessPathSupport.Root, new(db.Partition, queries.Reversed));

        await Assert.That(ordinary.Rows).IsEmpty();
        await Assert.That(reversed.Rows).IsEmpty();
        await Assert.That(reversed.AccessPath).IsEqualTo(indexed ? EqualityAccessPathSupport.IndexPath : EqualityAccessPathSupport.PointPath);
        await EqualityAccessPathSupport.SameRows(ordinary, reversed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql008NullEqualityNeverUsesTheIndexOrMatchesNullAndMissingRows(bool parameterized)
    {
        using var db = EqualityAccessPathSupport.Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        foreach (var reversed in new[] { false, true })
        {
            var request = parameterized
                ? EqualityAccessPathSupport.Request(db, indexed: true, parameterized: true, reversed) with
                { Parameters = new() { [EqualityAccessPathSupport.ParameterName] = JsonSerializer.SerializeToElement<object?>(null) } }
                : new QueryRequest(db.Partition, reversed ? ReversedNullIndex : NullIndex);
            await Assert.That(EqualityAccessPathSupport.Failure(engine, request).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            var page = engine.Execute(EqualityAccessPathSupport.Root, request with { AllowFullScan = true });
            await Assert.That(page.AccessPath).IsEqualTo(EqualityAccessPathSupport.FullScanPath);
            await Assert.That(page.Rows).IsEmpty();
        }
    }

    [Test]
    public async Task AcAisql008DisjunctiveEqualitiesCannotSelectOnlyOneNativePoint()
    {
        using var db = EqualityAccessPathSupport.Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(db.Partition, Disjunction);
        await Assert.That(EqualityAccessPathSupport.Failure(engine, request).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var page = engine.Execute(EqualityAccessPathSupport.Root, request with { AllowFullScan = true });
        await Assert.That(page.AccessPath).IsEqualTo(EqualityAccessPathSupport.FullScanPath);
        await Assert.That(string.Join(EqualityAccessPathSupport.Separator, page.Rows.Select(row => row.EntityId)))
            .IsEqualTo(EqualityAccessPathSupport.IndexedIds);
    }
}
