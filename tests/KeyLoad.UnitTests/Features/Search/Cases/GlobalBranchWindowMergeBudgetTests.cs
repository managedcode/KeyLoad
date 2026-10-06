using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GlobalBranchWindowMergeBudgetTests
{
    private const int MaximumCandidates = 2;
    private const int ExaminedCandidates = 3;
    private const int SelectedLimit = 1;

    [Test]
    public async Task AcRank003ExactNativeByteBoundaryChargesEveryExaminedDuplicate()
    {
        var candidate = GlobalBranchTestSupport.Candidate("shared", 0.8);
        var lower = GlobalBranchTestSupport.Candidate("lower", 0.7);
        var request = GlobalBranchTestSupport.Request(["left", "right"], limit: SelectedLimit);
        var windows = ImmutableArray.Create(
            GlobalBranchTestSupport.Window("left", [candidate, lower]),
            GlobalBranchTestSupport.Window("right", [candidate]));
        var exactBytes = GlobalBranchTestSupport.NativeInputBytes(request, windows);
        var limits = new DatabaseLimits
        {
            MaxBatchBytes = checked((int)exactBytes),
            MaxQueryReadBytes = exactBytes,
            MaxScanRecords = ExaminedCandidates,
            MaxResults = SelectedLimit
        };
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits));

        var result = GlobalBranchWindowMerger.Merge(request, windows, limits, budget);

        await Assert.That(result.ExaminedCandidateCount).IsEqualTo(ExaminedCandidates);
        await Assert.That(result.Candidates).HasSingleItem();
        await Assert.That(result.Truncated).IsTrue();
        await Assert.That(budget.ReadBytes).IsEqualTo(exactBytes);
    }

    [Test]
    public async Task AcRank003OneByteBelowNativeReadOrBatchBoundaryFailsBeforeResult()
    {
        var candidate = GlobalBranchTestSupport.Candidate("a", 1);
        var request = GlobalBranchTestSupport.Request(["w"], limit: MaximumCandidates);
        var windows = ImmutableArray.Create(GlobalBranchTestSupport.Window("w", [candidate]));
        var exactBytes = GlobalBranchTestSupport.NativeInputBytes(request, windows);
        var readLimits = new DatabaseLimits
        {
            MaxBatchBytes = checked((int)exactBytes),
            MaxQueryReadBytes = exactBytes - 1,
            MaxScanRecords = MaximumCandidates,
            MaxResults = MaximumCandidates
        };
        var batchLimits = readLimits with { MaxBatchBytes = checked((int)exactBytes - 1), MaxQueryReadBytes = long.MaxValue };

        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(request,
            windows, readLimits, new(UnitExecutionOptions.DatabaseLimits(readLimits))));
        var batchFailure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(request,
            windows, batchLimits, new(UnitExecutionOptions.DatabaseLimits(batchLimits))));

        await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(batchFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcRank003MalformedExaminedCandidateStillConsumesItsNativeBytes()
    {
        var request = GlobalBranchTestSupport.Request(["w"], limit: SelectedLimit);
        var windows = ImmutableArray.Create(GlobalBranchTestSupport.Window("w",
            [GlobalBranchTestSupport.Candidate("bad", double.NaN)]));
        var exactBytes = GlobalBranchTestSupport.NativeInputBytes(request, windows);
        var limits = new DatabaseLimits
        {
            MaxBatchBytes = checked((int)exactBytes),
            MaxQueryReadBytes = exactBytes,
            MaxScanRecords = MaximumCandidates,
            MaxResults = SelectedLimit
        };
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(request,
            windows, limits, budget));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(budget.ReadBytes).IsEqualTo(exactBytes);
    }

    [Test]
    public async Task AcRank003CandidateAndWindowCountsShareConfiguredRecordBound()
    {
        var limits = new DatabaseLimits { MaxScanRecords = MaximumCandidates, MaxResults = MaximumCandidates };
        var candidateRequest = GlobalBranchTestSupport.Request(["w"], limit: MaximumCandidates);
        var candidates = new[]
        {
            GlobalBranchTestSupport.Candidate("a", 0.9),
            GlobalBranchTestSupport.Candidate("b", 0.8),
            GlobalBranchTestSupport.Candidate("c", 0.7)
        };
        var candidateFailure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(
            candidateRequest, [GlobalBranchTestSupport.Window("w", candidates)], limits, new(UnitExecutionOptions.DatabaseLimits(limits))));
        var windowFailure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(
            GlobalBranchTestSupport.Request(["a", "b", "c"], limit: MaximumCandidates), [], limits, new(UnitExecutionOptions.DatabaseLimits(limits))));
        var receivedWindowFailure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchWindowMerger.Merge(
            GlobalBranchTestSupport.Request(["a", "b"], limit: MaximumCandidates),
            [GlobalBranchTestSupport.Window("a", []), GlobalBranchTestSupport.Window("b", []),
                GlobalBranchTestSupport.Window("a", [])], limits, new(UnitExecutionOptions.DatabaseLimits(limits))));

        await Assert.That(candidateFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(windowFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(receivedWindowFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcRank003CancellationIsObservedAndNextMergeRemainsHealthy()
    {
        var request = GlobalBranchTestSupport.Request(["w"]);
        var windows = ImmutableArray.Create(GlobalBranchTestSupport.Window("w",
            [GlobalBranchTestSupport.Candidate("a", 1)]));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var canceledBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()), cancellationToken: canceled.Token);

        Assert.ThrowsExactly<OperationCanceledException>(() => GlobalBranchWindowMerger.Merge(request,
            windows, new(), canceledBudget));
        var healthy = GlobalBranchTestSupport.Merge(request, windows);

        await Assert.That(healthy.Candidates).HasSingleItem();
    }
}
