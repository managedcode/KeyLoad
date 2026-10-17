using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantAdmissionRefusals
{
    private const string QueueOwnsCommand = "The command remains owned by its admission queue.";
    private const string InvalidQueuedResult = "true";
    private const string InvalidQueuedFailure = "A caller cannot fail queue-owned work.";
    internal static async Task RunAsync(TestDatabase fixture, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, AdmittedCommand original, CancellationToken token)
    {
        var state = TenantAdmissionAssertions.State(fixture.Store);
        var journal = fixture.ReadJournalCut();
        var charges = governor.Snapshot();
        await QueuedOwnershipAsync(fixture, governor, original, charges, state, journal);
        var excessive = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "refused");
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            TenantAdmissionOperations.Enqueue(fixture, inbox, excessive, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var principal = TenantAdmissionOperations.Principal(fixture, TenantAdmissionOperations.PrincipalB);
        failure = Assert.ThrowsExactly<KeyLoadException>(() => inbox.Enqueue(excessive, principal,
            RuntimeJournalBootstrapAdmissionFlow.PayloadBytes(excessive), cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        await selected.CancelAsync();
        var cancelled = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalB, "cancelled");
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            TenantAdmissionOperations.Enqueue(fixture, inbox, cancelled, selected.Token));
        var waitFailure = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            original.Completion.WaitAsync(selected.Token));
        ArgumentNullException.ThrowIfNull(waitFailure);
        await Assert.That(waitFailure.CancellationToken).IsEqualTo(selected.Token);
        await Assert.That(original.Completion.IsCompleted).IsFalse();
        await Assert.That(governor.Snapshot()).IsEqualTo(charges);
        await TenantAdmissionAssertions.UnchangedAsync(fixture, state, journal);
        await Assert.That(fixture.Database.GetDocument(TenantAdmissionOperations.PrincipalA,
            new(fixture.Partition, TenantAdmissionOperations.Collection, "refused"), cancellationToken: token)).IsNull();
        await Assert.That(fixture.Database.GetDocument(TenantAdmissionOperations.PrincipalB,
            new(TenantAdmissionOperations.PartitionB(fixture), TenantAdmissionOperations.Collection, "cancelled"), cancellationToken: token))
            .IsNull();
    }

    private static async Task QueuedOwnershipAsync(TestDatabase fixture, CommandAdmissionGovernor governor,
        AdmittedCommand original, CommandAdmissionSnapshot charges, string[] state,
        (long LastIndex, long CommittedIndex, long Term) journal)
    {
        original.Dispose();
        original.Dispose();
        var completed = Assert.ThrowsExactly<InvalidOperationException>(() => original.Complete(new(InvalidQueuedResult)));
        var failed = Assert.ThrowsExactly<InvalidOperationException>(() => original.Fail(new IOException(InvalidQueuedFailure)));
        await Assert.That(completed.Message).IsEqualTo(QueueOwnsCommand);
        await Assert.That(failed.Message).IsEqualTo(QueueOwnsCommand);
        await Assert.That(original.Completion.IsCompleted).IsFalse();
        await Assert.That(governor.Snapshot()).IsEqualTo(charges);
        await TenantAdmissionAssertions.UnchangedAsync(fixture, state, journal);
    }
}
