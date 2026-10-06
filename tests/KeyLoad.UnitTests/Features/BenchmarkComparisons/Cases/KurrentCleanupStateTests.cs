using Grpc.Core;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class KurrentCleanupStateTests
{
    private const int DeadlineStatus = 4;

    [Test]
    public async Task NestedFatalCleanupOverridesPrimaryWhileDiagnosticKeepsFirstFailure()
    {
        var state = new KurrentCleanupState(2, UnitBenchmarkOptions.Lifecycle());
        var primary = new RpcException(new Status(StatusCode.DeadlineExceeded, "primary"));
        await Assert.That(state.TrySubmit(CancellationToken.None, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(0);
        state.CompleteDelete(primary);
        var fatal = RuntimeOversizeFailure();
        var nested = new InvalidOperationException("outer", new AggregateException(
            new IOException("ordinary branch"), new ArgumentException("later branch", fatal)));
        state.RecordFailure(nested, KurrentCleanupStage.NativeDispose, disposal: true);
        state.FinishDeletion(cancellationRequested: true, expired: false);

        var diagnostic = state.Snapshot();
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.Delete);
        await Assert.That(diagnostic.Outcome).IsEqualTo(KurrentCleanupOutcome.Failed);
        await Assert.That(diagnostic.Reason).IsEqualTo(KurrentCleanupFailureReason.NativeRpc);
        await Assert.That(diagnostic.GrpcStatus).IsEqualTo(DeadlineStatus);
        await Assert.That(diagnostic.Counts).IsEqualTo(new KurrentCleanupCounts(2, 1, 0, 1, 0, 1));
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(1);
        await Assert.That(diagnostic.CancellationRequested).IsTrue();
        await Assert.That(ThrownBy<OutOfMemoryException>(state)).IsSameReferenceAs(fatal);
    }

    [Test]
    public async Task FirstFatalInAggregateOrderWinsOverSiblingAndLaterFailure()
    {
        var state = new KurrentCleanupState(0, UnitBenchmarkOptions.Lifecycle());
        var firstFatal = RuntimeOversizeFailure();
        var siblingFatal = RuntimeOversizeFailure();
        var laterFatal = RuntimeOversizeFailure();
        state.RecordFailure(new AggregateException(new InvalidOperationException("first branch", firstFatal),
            new InvalidOperationException("second branch", siblingFatal)), KurrentCleanupStage.NativeDispose);
        state.RecordFailure(new InvalidOperationException("later fatal", laterFatal),
            KurrentCleanupStage.HttpDispose, disposal: true);

        var diagnostic = state.Snapshot();
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.NativeDispose);
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(1);
        await Assert.That(ThrownBy<OutOfMemoryException>(state)).IsSameReferenceAs(firstFatal);
    }

    [Test]
    public async Task FirstOrdinaryFailureAndDiagnosticsSurviveLaterDisposalFailure()
    {
        var state = new KurrentCleanupState(1, UnitBenchmarkOptions.Lifecycle());
        var first = new IOException("first ordinary cleanup failure");
        state.RecordFailure(first, KurrentCleanupStage.HttpDispose);
        state.RecordFailure(new InvalidOperationException("later ordinary disposal failure"),
            KurrentCleanupStage.Drain, disposal: true);

        var diagnostic = state.Snapshot();
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.HttpDispose);
        await Assert.That(diagnostic.Reason).IsEqualTo(KurrentCleanupFailureReason.Unknown);
        await Assert.That(diagnostic.Counts).IsEqualTo(new KurrentCleanupCounts(1, 0, 0, 0, 0, 0));
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(1);
        await Assert.That(ThrownBy<IOException>(state)).IsSameReferenceAs(first);
    }

    [Test]
    public async Task FatalBeyondSixteenOrdinaryInnerLinksStillOverridesEarlierPrimary()
    {
        var state = new KurrentCleanupState(1, UnitBenchmarkOptions.Lifecycle());
        var primary = new RpcException(new Status(StatusCode.DeadlineExceeded, "primary"));
        state.RecordFailure(primary, KurrentCleanupStage.Delete);
        var fatal = RuntimeOversizeFailure();
        var nested = (Exception)fatal;
        for (var index = 0; index < 16; index++)
        {
            nested = new InvalidOperationException("nested", nested);
        }
        state.RecordFailure(nested, KurrentCleanupStage.NativeDispose, disposal: true);

        var diagnostic = state.Snapshot();
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.Delete);
        await Assert.That(diagnostic.Reason).IsEqualTo(KurrentCleanupFailureReason.NativeRpc);
        await Assert.That(diagnostic.GrpcStatus).IsEqualTo(DeadlineStatus);
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(1);
        await Assert.That(ThrownBy<OutOfMemoryException>(state)).IsSameReferenceAs(fatal);
    }

    [Test]
    public async Task EmptyCleanupRemainsSuccessful()
    {
        var state = new KurrentCleanupState(0, UnitBenchmarkOptions.Lifecycle());
        state.FinishDeletion(cancellationRequested: false, expired: false);

        var diagnostic = state.Snapshot();
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.Complete);
        await Assert.That(diagnostic.Outcome).IsEqualTo(KurrentCleanupOutcome.Succeeded);
        await Assert.That(diagnostic.Reason).IsEqualTo(KurrentCleanupFailureReason.None);
        await Assert.That(diagnostic.Counts.IsComplete).IsTrue();
        state.ThrowIfFailed();
    }

    private static TException? ThrownBy<TException>(KurrentCleanupState state) where TException : Exception
    {
        try
        {
            state.ThrowIfFailed();
        }
        catch (TException error)
        {
            return error;
        }
        return null;
    }

    private static OutOfMemoryException RuntimeOversizeFailure()
    {
        try
        {
            // The CLR rejects impossible dimensions before allocating an array payload.
            _ = Array.CreateInstance(typeof(byte), int.MaxValue, int.MaxValue);
        }
        catch (OutOfMemoryException error)
        {
            return error;
        }
        throw new InvalidOperationException("Runtime did not reject the impossible array dimensions.");
    }
}
