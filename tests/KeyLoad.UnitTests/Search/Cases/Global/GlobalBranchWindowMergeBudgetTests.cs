using System.Collections.Immutable;
using KeyLoad.Query.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class GlobalBranchWindowMergeBudgetTests
{
    private const int ExaminedCandidates = 3;

    [Test]
    public async Task AcRank003ExactByteBoundaryChargesEveryExaminedDuplicate()
    {
        var candidate = GlobalBranchTestSupport.Candidate("shared", 0.8);
        var lower = GlobalBranchTestSupport.Candidate("lower", 0.7);
        var request = GlobalBranchTestSupport.Request(["left", "right"], limit: 1);
        var windows = ImmutableArray.Create(
            GlobalBranchTestSupport.Window("left", [candidate, lower]),
            GlobalBranchTestSupport.Window("right", [candidate]));
        var exactBytes = GlobalBranchTestSupport.InputBytes(request, windows);
        var budget = new ReadExecutionBudget();

        var result = GlobalBranchWindowMerger.Merge(request, windows, budget);

        await Assert.That(result.ExaminedCandidateCount).IsEqualTo(ExaminedCandidates);
        await Assert.That(result.Candidates).HasSingleItem();
        await Assert.That(result.Truncated).IsTrue();
        await Assert.That(budget.ReadBytes).IsEqualTo(exactBytes);
    }

    [Test]
    public async Task AcRank003MalformedExaminedCandidateStillConsumesItsBytes()
    {
        var request = GlobalBranchTestSupport.Request(["w"], limit: 1);
        var windows = ImmutableArray.Create(GlobalBranchTestSupport.Window("w",
            [GlobalBranchTestSupport.Candidate("bad", double.NaN)]));
        var exactBytes = GlobalBranchTestSupport.InputBytes(request, windows);
        var budget = new ReadExecutionBudget();

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(request,
            windows, budget));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(budget.ReadBytes).IsEqualTo(exactBytes);
    }

    [Test]
    public async Task AcRank003CancellationIsObservedAndNextMergeRemainsHealthy()
    {
        var request = GlobalBranchTestSupport.Request(["w"]);
        var windows = ImmutableArray.Create(GlobalBranchTestSupport.Window("w",
            [GlobalBranchTestSupport.Candidate("a", 1)]));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var canceledBudget = new ReadExecutionBudget(cancellationToken: canceled.Token);

        Assert.ThrowsExactly<OperationCanceledException>(() => GlobalBranchWindowMerger.Merge(request,
            windows, canceledBudget));
        var healthy = GlobalBranchTestSupport.Merge(request, windows);

        await Assert.That(healthy.Candidates).HasSingleItem();
    }
}
