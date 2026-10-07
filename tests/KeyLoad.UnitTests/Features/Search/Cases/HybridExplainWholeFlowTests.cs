using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class HybridExplainWholeFlowTests
{
    private const int WorkingBytes = 128;
    private const string CaptureExceeded = "The search explanation byte budget is exceeded.";
    private const string FieldDenied = "The query requires a protected field-use grant.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualWeightedBranchesExplainLiteralRanksAndEligibleCandidates(bool restricted)
    {
        using var db = HybridExplainWholeFlow.Create();
        var engine = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = HybridExplainWholeFlow.Request(db, restricted);
        var before = HybridExplainWholeFlow.Snapshot(db);
        var position = db.Store.Position;
        var token = TestContext.Current!.Execution.CancellationToken;
        var actual = await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader, request, token);
        await HybridExplainWholeFlow.LiteralAsync(actual, db.Partition, restricted);
        var plain = await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            request with { Search = request.Search with { Explain = false } }, token);
        await Assert.That(JsonDefaults.Serialize(plain.Hits).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            actual.Hits.Select(hit => hit with { Explanation = null }).ToArray()))).IsTrue();
        await Assert.That(HybridExplainWholeFlow.Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task DeniedExplainAndOriginalCancellationReturnNoPartialResultBeforeHealthyLiteralRead()
    {
        var clock = new HybridExplainObservedWorkClock();
        using var db = HybridExplainWholeFlow.Create(clock);
        var engine = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = HybridExplainWholeFlow.Request(db, false);
        var before = HybridExplainWholeFlow.Snapshot(db);
        var position = db.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            ThreeWayHybridTestSupport.TextOnlyReader, request, TestContext.Current!.Execution.CancellationToken))
            ?? throw new InvalidOperationException("The actual denied Explain did not fail.");
        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(error.Message).IsEqualTo(FieldDenied);
        using var canceled = new CancellationTokenSource();
        var nativeBefore = db.Store.GetReadDiagnostics();
        clock.Arm(() => db.Store.GetReadDiagnostics().RangeExaminedBytes > nativeBefore.RangeExaminedBytes,
            canceled.Cancel);
        GraphSearchResult? partial = null;
        OperationCanceledException cancellation;
        try
        {
            cancellation = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                partial = await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader, request, canceled.Token))
                ?? throw new InvalidOperationException("The actual canceled Explain did not fail.");
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(db.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(nativeBefore.RangeExaminedBytes);
        await Assert.That(cancellation.CancellationToken).IsEqualTo(canceled.Token);
        await Assert.That(partial).IsNull();
        await Assert.That(HybridExplainWholeFlow.Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await HybridExplainWholeFlow.LiteralAsync(await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            request, TestContext.Current!.Execution.CancellationToken), db.Partition, false);
        await Assert.That(HybridExplainWholeFlow.Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }
    [Test]
    public async Task ActualExplainCaptureByteCapRejectsWithoutPartialOrEffectAndHealthyReadContinues()
    {
        using var db = HybridExplainWholeFlow.Create();
        var before = HybridExplainWholeFlow.Snapshot(db);
        var position = db.Store.Position;
        var request = HybridExplainWholeFlow.Request(db, false);
        var limited = new SearchEngine(db.Database,
            UnitExecutionOptions.QueryExecution(new() { MaximumResultBytes = WorkingBytes }));
        GraphSearchResult? partial = null;
        var token = TestContext.Current!.Execution.CancellationToken;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            partial = await limited.GraphSearchAsync(ThreeWayHybridTestSupport.Reader, request, token))
            ?? throw new InvalidOperationException("The actual bounded Explain capture did not fail.");
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo(CaptureExceeded);
        await Assert.That(partial).IsNull();
        await Assert.That(HybridExplainWholeFlow.Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await HybridExplainWholeFlow.LiteralAsync(await new SearchEngine(db.Database,
            UnitExecutionOptions.QueryExecution()).GraphSearchAsync(ThreeWayHybridTestSupport.Reader, request, token),
            db.Partition, false);
        await Assert.That(HybridExplainWholeFlow.Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }

}
