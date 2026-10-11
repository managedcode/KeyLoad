using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantAdmissionRefusals
{
    internal static async Task RunAsync(TestDatabase fixture, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, AdmittedCommand original, CancellationToken token)
    {
        var state = TenantAdmissionAssertions.State(fixture.Store);
        var journal = fixture.ReadJournalCut();
        var charges = governor.Snapshot();
        var excessive = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "refused");
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            TenantAdmissionOperations.Enqueue(fixture, inbox, excessive, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var principal = TenantAdmissionOperations.Principal(fixture, TenantAdmissionOperations.PrincipalB);
        failure = Assert.ThrowsExactly<KeyLoadException>(() => inbox.Enqueue(excessive, principal,
            RuntimeJournalBootstrapAdmissionFlow.PayloadBytes(excessive), token));
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
}
