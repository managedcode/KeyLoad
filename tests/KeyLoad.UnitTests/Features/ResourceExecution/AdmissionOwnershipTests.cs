using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AdmissionOwnershipTests
{
    private const string PrincipalId = "principal";
    private const string TenantId = "tenant";
    private const string RequestPath = "/v1/commands";
    private const string QueueStoppedDetail = "The node stopped before returning a command outcome. Query or retry the same command ID.";
    private const string SuccessfulResultJson = "true";
    private const string EmptyOperationJson = "{}";
    private const int WaitSeconds = 5;

    [Test]
    public async Task InvalidCommandReservationArgumentsLeaveEveryCounterUnchanged()
    {
        var governor = new CommandAdmissionGovernor();
        var principal = NewPrincipal();

        Assert.ThrowsExactly<ArgumentNullException>(() => governor.Reserve(OperationKind.Batch, null!, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => governor.Reserve(OperationKind.Batch, principal, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => governor.Reserve(OperationKind.Batch, principal, 0, -1));
        Assert.ThrowsExactly<ArgumentException>(() => governor.Reserve(OperationKind.Batch, principal with { Id = string.Empty }, 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => governor.Reserve(OperationKind.Batch, principal with { TenantId = string.Empty }, 0, 0));
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }

    [Test]
    public async Task InboxRejectsConcurrentReaderWithoutConsumingTheQueuedSignal()
    {
        await using var inbox = new AdmittedCommandInbox(new CommandAdmissionGovernor());
        using var cancellation = new CancellationTokenSource();
        var reader = inbox.ReadAsync(cancellation.Token).AsTask();

        await Assert.That(() => inbox.ReadAsync(cancellation.Token).AsTask()).Throws<InvalidOperationException>();
        var command = inbox.Enqueue(NewOperation(), NewPrincipal(), 0);

        await Assert.That(await reader.WaitAsync(TimeSpan.FromSeconds(WaitSeconds))).IsSameReferenceAs(command);
        command.Complete(new(SuccessfulResultJson));
    }

    [Test]
    public async Task AsyncDisposalStopsAndDrainsRegisteredReaderBeforeRejectingFutureWork()
    {
        var governor = new CommandAdmissionGovernor();
        await using var inbox = new AdmittedCommandInbox(governor);
        using var cancellation = new CancellationTokenSource();
        var reader = inbox.ReadAsync(cancellation.Token).AsTask();
        var firstDisposal = inbox.DisposeAsync().AsTask();
        var secondDisposal = inbox.DisposeAsync().AsTask();

        await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(WaitSeconds));
        await Assert.That(await reader.WaitAsync(TimeSpan.FromSeconds(WaitSeconds))).IsNull();
        await Assert.That(() => inbox.ReadAsync(cancellation.Token).AsTask()).Throws<ObjectDisposedException>();
        Assert.ThrowsExactly<ObjectDisposedException>(() => inbox.Enqueue(NewOperation(), NewPrincipal(), 0));
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }

    [Test]
    public async Task PreCancelledReadIsRejectedBeforeReaderOwnershipEvenAfterStop()
    {
        await using var inbox = new AdmittedCommandInbox(new CommandAdmissionGovernor());
        inbox.Stop();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.That(() => inbox.ReadAsync(cancellation.Token).AsTask()).Throws<OperationCanceledException>();
        await Assert.That(await inbox.ReadAsync()).IsNull();
    }

    [Test]
    public async Task DisposingUnresolvedDispatchedCommandReportsUnknownOutcomeAndReleasesOnce()
    {
        var governor = new CommandAdmissionGovernor();
        await using var inbox = new AdmittedCommandInbox(governor);
        var command = inbox.Enqueue(NewOperation(), NewPrincipal(), 0);
        await Assert.That(await inbox.ReadAsync()).IsSameReferenceAs(command);

        command.Dispose();
        command.Dispose();
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => command.Completion);

        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(failure.Message).IsEqualTo(QueueStoppedDetail);
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
        inbox.Stop();
    }

    [Test]
    public async Task HttpAdmissionRejectsInvalidPathFramingAndPrincipalBeforeReservation()
    {
        var governor = new HttpAdmissionGovernor();

        Assert.ThrowsExactly<ArgumentNullException>(() => governor.Begin(null!, 0));
        Assert.ThrowsExactly<KeyLoadException>(() => governor.Begin(RequestPath, -1));
        using var lease = governor.Begin(RequestPath, 0);
        Assert.ThrowsExactly<ArgumentNullException>(() => lease.Bind(null!));
        await Assert.That(governor.Status().VerifiedScopes).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
    }

    private static PrincipalRecord NewPrincipal() => new(PrincipalId, TenantId, [], []);

    private static ReplicatedOperation NewOperation() => new(Guid.NewGuid(), OperationKind.Batch, PrincipalId,
        TimeProvider.System.GetUtcNow(), EmptyOperationJson);
}
