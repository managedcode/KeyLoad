using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsNativeCancellation
{
    internal static async Task RunAsync(OperationalAnalyticsNativeOwner owner, OperationalAnalyticsOwner analytics,
        DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var failures = new List<Exception>();
        OperationalAnalyticsReport? report = null;
        OperationCanceledException? canceled = null;
        var originalWait = analytics.ReadAsync(from, to, selected.Token);
        await selected.CancelAsync();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                report = await originalWait;
            }
            catch (OperationCanceledException failure) when (failure.CancellationToken == selected.Token)
            {
                canceled = failure;
            }
        }, failures);
        // No healthy query replaces the original accepted operation before actual joined disposal.
        await ServerFailureObserver.ObserveAsync(owner.CloseAsync, failures);
        ServerFailureObserver.Observe(token.ThrowIfCancellationRequested, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(analytics.HasJoinedEngine).IsTrue();
            if (report is not null)
            {
                await OperationalAnalyticsNativeAssertions.ReportValueAsync(report, from, to, originalOwner: false);
            }
            else
            {
                ArgumentNullException.ThrowIfNull(canceled);
                await Assert.That(canceled.CancellationToken).IsEqualTo(selected.Token);
                await TestContext.Current!.OutputWriter.WriteLineAsync(P.WaitCancellationObserved);
            }
            if (analytics.ObservedCallerCancellation is { } observed)
            {
                // Native Query may preserve its own requested linked timeout token.
                await Assert.That(observed.CancellationToken.CanBeCanceled).IsTrue();
                await Assert.That(observed.CancellationToken.IsCancellationRequested).IsTrue();
                await Assert.That(selected.IsCancellationRequested).IsTrue();
                await TestContext.Current!.OutputWriter.WriteLineAsync(P.OriginalCancellationObserved);
            }
            else
            {
                await TestContext.Current!.OutputWriter.WriteLineAsync(P.OriginalCompleted);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
