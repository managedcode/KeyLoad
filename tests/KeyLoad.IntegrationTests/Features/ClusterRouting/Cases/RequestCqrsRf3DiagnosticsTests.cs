using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsRf3DiagnosticsTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(45);

    [Test]
    public Task MalformedAspireLogLinesAreExcludedFromTheBoundedArtifact()
        => RunAsync(RejectMalformedLinesAsync);

    [Test]
    public Task EachAspireNodeRetainsOnlyThirtyTwoClosedRecords()
        => RunAsync(VerifyPerNodeCapAsync);

    [Test]
    public Task IndependentNativeResourceConsumerDoesNotBlockOwnedCaptureJoin()
        => RunAsync(VerifyIndependentNativeConsumerAsync);

    [Test]
    public Task SuccessEvidenceWaitsForOriginalSubscriptionsAndIsMemoized()
        => RunAsync(VerifySuccessEvidenceLifecycleAsync);

    [Test]
    public Task ExactCallerCancellationAtAdmissionSettlesEveryOriginalCapture()
        => VerifyExactCallerCancellationAsync();

    [Test]
    public Task NativeEarlyStreamCompletionFailsWithoutPublishingEvidence()
        => VerifyNativeEarlyCompletionAsync();

    private static async Task RunAsync(Func<RequestCqrsRf3DiagnosticsTestScope, CancellationToken, Task> scenario)
    {
        using var deadline = new CancellationTokenSource(TestDeadline, TimeProvider.System);
        var failures = new List<Exception>();
        var lifecycle = new RequestCqrsLifecycleEvidence();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var scope = new RequestCqrsRf3DiagnosticsTestScope(Guid.NewGuid(), lifecycle);
            await ServerFailureObserver.ObserveAsync(() => scenario(scope, deadline.Token), failures)
                .ConfigureAwait(false);
            lifecycle.RecordFirstFailureIfAny(failures);
            await ServerFailureObserver.ObserveAsync(() => scope.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            lifecycle.RecordFirstFailureIfAny(failures);
        }, failures).ConfigureAwait(false);
        if (failures.Count > 0)
        { lifecycle.RecordFirstFailure(); }
        lifecycle.RecordTerminal();
        lifecycle.ThrowWithContext(failures);
    }

    private static async Task RejectMalformedLinesAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken).ConfigureAwait(false);
        scope.EmitMalformedThenOneValidPerNode();
        var artifact = await scope.CompleteAndReadArtifactAsync(cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(artifact, scope.WaveId, 1)
            .ConfigureAwait(false);
    }

    private static async Task VerifyPerNodeCapAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken).ConfigureAwait(false);
        scope.EmitFortyValidLinesPerNode();
        var artifact = await scope.CompleteAndReadArtifactAsync(cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(artifact, scope.WaveId, 32)
            .ConfigureAwait(false);
        await AssertCompletedCaptureObservationsAsync(scope.ReadLifecycleSnapshot()).ConfigureAwait(false);
    }

    private static async Task VerifyIndependentNativeConsumerAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken, startIndependentConsumer: true).ConfigureAwait(false);
        scope.EmitMalformedThenOneValidPerNode();
        await scope.Capture.DisposeAsync().ConfigureAwait(false);
        await scope.IndependentConsumer.EmitAndObserveAsync("join-oracle-after-capture-join", cancellationToken)
            .ConfigureAwait(false);
        await scope.JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        var joined = scope.Capture.ReadLifecycleSnapshot();
        await Assert.That(joined.Node1).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(joined.Node2).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(joined.Node3).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(IsPending(scope.IndependentConsumer.ReadLifecycleSnapshot().PendingMove)).IsTrue();
        var artifact = await RequestCqrsRf3DiagnosticsArtifactFiles.WriteAndReadAsync(scope.Capture,
            scope.OwnedArtifactPath, cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(artifact, scope.WaveId, 1)
            .ConfigureAwait(false);
        await AssertCompletedCaptureObservationsAsync(scope.ReadLifecycleSnapshot()).ConfigureAwait(false);
    }

    private static async Task VerifySuccessEvidenceLifecycleAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken).ConfigureAwait(false);
        scope.EmitMalformedThenOneValidPerNode();
        await RequestCqrsRf3DiagnosticsSuccessEvidence.AssertUnavailableBeforeJoinAsync(scope)
            .ConfigureAwait(false);
        var evidence = await RequestCqrsRf3DiagnosticsSuccessEvidence.CompleteAndReadMemoizedAsync(scope,
            cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(evidence.Bytes, scope.WaveId, 1)
            .ConfigureAwait(false);
        var lifecycle = scope.Capture.ReadLifecycleSnapshot();
        await Assert.That(lifecycle.Node1).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(lifecycle.Node2).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(lifecycle.Node3).IsEqualTo(TaskStatus.RanToCompletion);
        await AssertCompletedCaptureObservationsAsync(scope.ReadLifecycleSnapshot()).ConfigureAwait(false);
    }
    private static async Task VerifyExactCallerCancellationAsync()
    {
        using var caller = new CancellationTokenSource(TestDeadline, TimeProvider.System);
        var lifecycle = new RequestCqrsLifecycleEvidence();
        await using var scope = new RequestCqrsRf3DiagnosticsTestScope(Guid.NewGuid(), lifecycle);
        var failures = new List<Exception>();
        await scope.StartAsync(caller.Token).ConfigureAwait(false);
        var originalAdmission = scope.WaitForSubscriberStateAsync(false);
        await Assert.That(originalAdmission.IsCompleted).IsFalse();
        await caller.CancelAsync().ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => originalAdmission, failures).ConfigureAwait(false);
        lifecycle.RecordFirstFailure();
        await scope.DisposeAsync().ConfigureAwait(false);
        lifecycle.RecordTerminal();
        var snapshot = lifecycle.Snapshot();
        await Assert.That(failures.Count > 0 && failures.All(error => error is OperationCanceledException)).IsTrue();
        await Assert.That(snapshot.Stage).IsEqualTo(RequestCqrsLifecycleStage.OwnedRootCleanup);
        await Assert.That(snapshot.CallerCancellationRequested).IsTrue();
        await Assert.That(snapshot.ObserverCancellationRequested).IsTrue();
        await Assert.That(snapshot.AdmissionMove).IsEqualTo(TaskStatus.Canceled);
        await AssertThreeCapturesCompletedAsync(snapshot).ConfigureAwait(false);
        await AssertCompletedCaptureObservationsAsync(snapshot).ConfigureAwait(false);
        await Assert.That(File.Exists(scope.OwnedArtifactPath)).IsFalse();
        var context = lifecycle.FormatBoundedContext();
        await Assert.That(context.Contains("first{s=SubscriberAdmission", StringComparison.Ordinal)).IsTrue();
        await Assert.That(context.Contains("a=Canceled", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task VerifyNativeEarlyCompletionAsync()
    {
        using var deadline = new CancellationTokenSource(TestDeadline, TimeProvider.System);
        var lifecycle = new RequestCqrsLifecycleEvidence();
        await using var scope = new RequestCqrsRf3DiagnosticsTestScope(Guid.NewGuid(), lifecycle);
        var failures = new List<Exception>();
        await scope.StartAsync(deadline.Token).ConfigureAwait(false);
        scope.CompleteResourceStream(RequestCqrsRf3Protocol.Node1);
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => scope.Capture.WaitForRecordAsync(
            RequestCqrsRf3Protocol.Node1, McpTransportStage.BodyMethodMismatch,
            McpTransportMethodCategory.ToolsCall, deadline.Token), failures,
            lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.CaptureJoin).ConfigureAwait(false);
        var beforeCleanup = lifecycle.FirstFailureSnapshot
            ?? throw new InvalidOperationException("The native early-completion failure snapshot was absent.");
        await Assert.That(beforeCleanup.Stage).IsEqualTo(RequestCqrsLifecycleStage.CaptureJoin);
        await Assert.That(beforeCleanup.CaptureFallbackRequested).IsFalse();
        await Assert.That(beforeCleanup.CaptureNode1.HasValue
            && beforeCleanup.CaptureNode2.HasValue && beforeCleanup.CaptureNode3.HasValue).IsTrue();
        await Assert.That(beforeCleanup.CaptureNode2).IsNotEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(beforeCleanup.CaptureNode3).IsNotEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(failures.Count == 1 && failures[0] is InvalidOperationException).IsTrue();
        await scope.DisposeAsync().ConfigureAwait(false);
        lifecycle.RecordTerminal();
        var terminal = lifecycle.Snapshot();
        await AssertThreeCapturesCompletedAsync(terminal).ConfigureAwait(false);
        await Assert.That(beforeCleanup.ScopeCompletion.ExplicitNode1.State)
            .IsEqualTo(RequestCqrsCompletionCallState.Returned);
        await Assert.That(beforeCleanup.ScopeCompletion.ExplicitNode1.StatusAtStart.HasValue).IsTrue();
        await Assert.That(beforeCleanup.ScopeCompletion.ExplicitNode1.StatusAtReturn.HasValue).IsTrue();
        await AssertReturnedCallAsync(terminal.ScopeCompletion.BatchNode2).ConfigureAwait(false);
        await AssertReturnedCallAsync(terminal.ScopeCompletion.BatchNode3).ConfigureAwait(false);
        await AssertReturnedCallAsync(terminal.CleanupCompletion.Node1).ConfigureAwait(false);
        await AssertReturnedCallAsync(terminal.CleanupCompletion.Node2).ConfigureAwait(false);
        await AssertReturnedCallAsync(terminal.CleanupCompletion.Node3).ConfigureAwait(false);
        await Assert.That(terminal.CleanupCompletion.Drain.OriginalJoined).IsTrue();
        await Assert.That(terminal.CaptureNode2).IsNotEqualTo(beforeCleanup.CaptureNode2);
        await Assert.That(terminal.CaptureNode3).IsNotEqualTo(beforeCleanup.CaptureNode3);
        await Assert.That(File.Exists(scope.OwnedArtifactPath)).IsFalse();
        var context = lifecycle.FormatBoundedContext();
        await Assert.That(context.Contains("first{s=CaptureJoin", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertThreeCapturesCompletedAsync(RequestCqrsLifecycleSnapshot snapshot)
    {
        await Assert.That(snapshot.CaptureNode1).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(snapshot.CaptureNode2).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(snapshot.CaptureNode3).IsEqualTo(TaskStatus.RanToCompletion);
    }

    private static async Task AssertCompletedCaptureObservationsAsync(RequestCqrsLifecycleSnapshot snapshot)
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

    private static async Task AssertReturnedCallAsync(RequestCqrsCompletionCallSnapshot call)
    {
        await Assert.That(call.State).IsEqualTo(RequestCqrsCompletionCallState.Returned);
        await Assert.That(call.StatusAtStart.HasValue).IsTrue();
        await Assert.That(call.StatusAtReturn.HasValue).IsTrue();
    }

    private static bool IsPending(TaskStatus? status) => status is TaskStatus.Created
        or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun or TaskStatus.Running
        or TaskStatus.WaitingForChildrenToComplete;

}
