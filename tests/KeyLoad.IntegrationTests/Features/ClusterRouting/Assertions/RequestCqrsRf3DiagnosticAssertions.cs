namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3DiagnosticAssertions
{
    internal static async Task AssertThreeCapturesCompletedAsync(RequestCqrsLifecycleSnapshot snapshot)
    {
        await Assert.That(snapshot.CaptureNode1).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(snapshot.CaptureNode2).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(snapshot.CaptureNode3).IsEqualTo(TaskStatus.RanToCompletion);
    }

    internal static async Task AssertCompletedCaptureObservationsAsync(RequestCqrsLifecycleSnapshot snapshot)
    {
        await AssertReturnedCallAsync(snapshot.ScopeCompletion.BatchNode1).ConfigureAwait(false);
        await AssertReturnedCallAsync(snapshot.ScopeCompletion.BatchNode2).ConfigureAwait(false);
        await AssertReturnedCallAsync(snapshot.ScopeCompletion.BatchNode3).ConfigureAwait(false);
        await AssertReturnedCallAsync(snapshot.CleanupCompletion.Node1).ConfigureAwait(false);
        await AssertReturnedCallAsync(snapshot.CleanupCompletion.Node2).ConfigureAwait(false);
        await AssertReturnedCallAsync(snapshot.CleanupCompletion.Node3).ConfigureAwait(false);
        await Assert.That(snapshot.CleanupCompletion.Drain.Started).IsTrue();
        await Assert.That(snapshot.CleanupCompletion.Drain.TokenCanceledAtStart).IsFalse();
        await Assert.That(snapshot.CleanupCompletion.Drain.Returned).IsTrue();
        await Assert.That(snapshot.CleanupCompletion.Drain.OriginalJoined).IsTrue();
        await Assert.That(snapshot.CleanupCompletion.Drain.FallbackEntered).IsFalse();
    }

    internal static async Task AssertReturnedCallAsync(RequestCqrsCompletionCallSnapshot call)
    {
        await Assert.That(call.State).IsEqualTo(RequestCqrsCompletionCallState.Returned);
        await Assert.That(call.StatusAtStart.HasValue).IsTrue();
        await Assert.That(call.StatusAtReturn.HasValue).IsTrue();
    }

}
