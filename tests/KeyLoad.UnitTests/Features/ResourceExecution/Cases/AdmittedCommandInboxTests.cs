using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AdmittedCommandInboxTests
{
    private static AdmittedCommand Enqueue(AdmittedCommandInbox queue, ReplicatedOperation operation, PrincipalRecord principal, int bytes)
        => queue.Enqueue(operation, principal, bytes, TestContext.Current!.Execution.CancellationToken);
    private static PrincipalRecord Principal() => new("p", "tenant", [], []);
    private static ReplicatedOperation Operation(OperationKind kind = OperationKind.Batch)
        => new(Guid.NewGuid(), kind, "p", TimeProvider.System.GetUtcNow(), "{}");
    [Test]
    public async Task ActiveCommandsRetainCapacityAndAcknowledgementsHaveTheirOwnLane()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command(new() { MaxCommands = 1 }));
        await using var queue = new AdmittedCommandInbox(governor);
        var data = Enqueue(queue, Operation(), Principal(), 2);
        var acknowledgement = Enqueue(queue, Operation(OperationKind.Delivery), Principal(), 2);
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(acknowledgement);
        acknowledgement.Complete(new("true"));
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(data);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Enqueue(queue, Operation(), Principal(), 2)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        data.Complete(new("true"));
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(0);
        var next = Enqueue(queue, Operation(), Principal(), 2);
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(next);
        next.Complete(new("true"));
        queue.Stop();
    }
    [Test]
    public async Task AControlFloodCannotStarveWaitingDataAndEachLanePreservesFifo()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command(new() { MaxPrincipalControlCommands = 16, MaxTenantControlCommands = 16 }));
        await using var queue = new AdmittedCommandInbox(governor);
        var data = Enumerable.Range(0, 2).Select(_ => Enqueue(queue, Operation(), Principal(), 2)).ToArray();
        var control = Enumerable.Range(0, 10).Select(_ => Enqueue(queue, Operation(OperationKind.Membership), Principal(), 2)).ToArray();
        var expected = control.Take(8).Append(data[0]).Concat(control.Skip(8)).Append(data[1]).ToArray();
        foreach (var pending in expected)
        { await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(pending); pending.Complete(new("true")); }
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
        queue.Stop();
    }
    [Test]
    public async Task CancellingAResponseDoesNotReleaseAnAcceptedCommandsReservation()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command(new() { MaxCommands = 1 }));
        await using var queue = new AdmittedCommandInbox(governor);
        var pending = Enqueue(queue, Operation(), Principal(), 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(async () => { await pending.Completion.WaitAsync(cancellation.Token); }).Throws<OperationCanceledException>();
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(pending);
        pending.Complete(new("true"));
        await Assert.That((await pending.Completion).Json).IsEqualTo("true");
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(0);
        queue.Stop();
    }
    [Test]
    public async Task ShutdownRejectsQueuedCommandsReleasesTheirReservationsAndWakesTheReader()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
        await using var queue = new AdmittedCommandInbox(governor);
        var active = Enqueue(queue, Operation(), Principal(), 2);
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(active);
        var queued = Enqueue(queue, Operation(), Principal(), 2);
        var control = Enqueue(queue, Operation(OperationKind.Delivery), Principal(), 2);
        queue.Stop();
        queue.Stop();
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsNull();
        foreach (var pending in new[] { queued, control })
        {
            await Assert.That((await Assert.ThrowsExactlyAsync<KeyLoadException>(() => pending.Completion))!.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        }

        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Enqueue(queue, Operation(), Principal(), 2)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        active.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, "Shutdown interrupted the active command."));
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => active.Completion);
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }
    [Test]
    public async Task CancelledReadersAndMismatchedPrincipalClaimsCannotConsumeQueuedWorkOrBypassScopes()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
        await using var queue = new AdmittedCommandInbox(governor);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            Enqueue(queue, Operation() with { PrincipalId = "other" }, Principal(), 2)).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(0);
        var pending = Enqueue(queue, Operation(), Principal(), 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(() => queue.ReadAsync(cancellation.Token).AsTask()).Throws<OperationCanceledException>();
        await Assert.That(await queue.ReadAsync(TestContext.Current!.Execution.CancellationToken)).IsSameReferenceAs(pending);
        pending.Complete(new("true"));
        queue.Stop();
    }
}
