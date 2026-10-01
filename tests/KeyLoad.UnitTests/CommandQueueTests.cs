using KeyLoad.Core;

namespace KeyLoad.UnitTests;

public sealed class CommandQueueTests
{
    private static AdmittedCommandQueue.Pending Enqueue(AdmittedCommandQueue queue, ReplicatedOperation operation, PrincipalRecord principal, int bytes)
        => queue.Enqueue(operation, principal, bytes, TestContext.Current.CancellationToken);
    private static PrincipalRecord Principal() => new("p", "tenant", [], []);
    private static ReplicatedOperation Operation(OperationKind kind = OperationKind.Batch)
        => new(Guid.NewGuid(), kind, "p", DateTimeOffset.UtcNow, "{}");
    [Fact]
    public async Task ActiveCommandsRetainCapacityAndAcknowledgementsHaveTheirOwnLane()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxCommands = 1 }); var queue = new AdmittedCommandQueue(governor);
        var data = Enqueue(queue, Operation(), Principal(), 2); var acknowledgement = Enqueue(queue, Operation(OperationKind.Delivery), Principal(), 2);
        Assert.Same(acknowledgement, await queue.ReadAsync(TestContext.Current.CancellationToken)); acknowledgement.Complete(new("true"));
        Assert.Same(data, await queue.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Enqueue(queue, Operation(), Principal(), 2)).Code);
        data.Complete(new("true")); Assert.Equal(0, governor.Snapshot().Commands);
        var next = Enqueue(queue, Operation(), Principal(), 2); Assert.Same(next, await queue.ReadAsync(TestContext.Current.CancellationToken));
        next.Complete(new("true")); queue.Stop();
    }
    [Fact]
    public async Task AControlFloodCannotStarveWaitingDataAndEachLanePreservesFifo()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxPrincipalControlCommands = 16, MaxTenantControlCommands = 16 });
        var queue = new AdmittedCommandQueue(governor);
        var data = Enumerable.Range(0, 2).Select(_ => Enqueue(queue, Operation(), Principal(), 2)).ToArray();
        var control = Enumerable.Range(0, 10).Select(_ => Enqueue(queue, Operation(OperationKind.Membership), Principal(), 2)).ToArray();
        var expected = control.Take(8).Append(data[0]).Concat(control.Skip(8)).Append(data[1]).ToArray();
        foreach (var pending in expected) { Assert.Same(pending, await queue.ReadAsync(TestContext.Current.CancellationToken)); pending.Complete(new("true")); }
        Assert.Equal(new(0, 0, 0, 0, 0, 0), governor.Snapshot()); queue.Stop();
    }
    [Fact]
    public async Task CancellingAResponseDoesNotReleaseAnAcceptedCommandsReservation()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxCommands = 1 }); var queue = new AdmittedCommandQueue(governor);
        var pending = Enqueue(queue, Operation(), Principal(), 2);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Completion.WaitAsync(cancellation.Token));
        Assert.Equal(1, governor.Snapshot().Commands);
        Assert.Same(pending, await queue.ReadAsync(TestContext.Current.CancellationToken)); pending.Complete(new("true"));
        Assert.Equal("true", (await pending.Completion).Json); Assert.Equal(0, governor.Snapshot().Commands); queue.Stop();
    }
    [Fact]
    public async Task ShutdownRejectsQueuedCommandsReleasesTheirReservationsAndWakesTheReader()
    {
        var governor = new CommandAdmissionGovernor(); var queue = new AdmittedCommandQueue(governor);
        var active = Enqueue(queue, Operation(), Principal(), 2); Assert.Same(active, await queue.ReadAsync(TestContext.Current.CancellationToken));
        var queued = Enqueue(queue, Operation(), Principal(), 2);
        var control = Enqueue(queue, Operation(OperationKind.Delivery), Principal(), 2);
        queue.Stop(); queue.Stop(); Assert.Null(await queue.ReadAsync(TestContext.Current.CancellationToken));
        foreach (var pending in new[] { queued, control })
            Assert.Equal(ErrorCode.UnknownWriteOutcome, (await Assert.ThrowsAsync<KeyLoadException>(() => pending.Completion)).Code);
        Assert.Equal(1, governor.Snapshot().Commands); Assert.Equal(0, governor.Snapshot().ControlCommands);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Enqueue(queue, Operation(), Principal(), 2)).Code);
        active.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, "Shutdown interrupted the active command."));
        await Assert.ThrowsAsync<KeyLoadException>(() => active.Completion);
        Assert.Equal(new(0, 0, 0, 0, 0, 0), governor.Snapshot());
    }
    [Fact]
    public async Task CancelledReadersAndMismatchedPrincipalClaimsCannotConsumeQueuedWorkOrBypassScopes()
    {
        var governor = new CommandAdmissionGovernor(); var queue = new AdmittedCommandQueue(governor);
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() =>
            Enqueue(queue, Operation() with { PrincipalId = "other" }, Principal(), 2)).Code);
        Assert.Equal(0, governor.Snapshot().Commands);
        var pending = Enqueue(queue, Operation(), Principal(), 2); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.ReadAsync(cancellation.Token).AsTask());
        Assert.Same(pending, await queue.ReadAsync(TestContext.Current.CancellationToken)); pending.Complete(new("true")); queue.Stop();
    }
}
