using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantShutdownWholeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        TenantAdmissionOperations.Provision(fixture);
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
        var original = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "a1");
        var state = TenantAdmissionAssertions.State(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        await using var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
        await using var selected = owner.Acquire(original.CommandId, NativeRequestWorkKind.CommandCapability);
        using var admission = TenantAdmissionOperations.Reserve(fixture, governor, original, token);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = selected.Cancellation.Register(() => cancelled.TrySetResult());
        var drain = owner.DrainAsync();
        await cancelled.Task.WaitAsync(new NativeRuntimeTestOptions().CompletionTimeout, token);
        await Assert.That(drain.IsCompleted).IsFalse();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            fixture.SubmitIssuedEmbedded(original, selected.Cancellation));
        var refused = Assert.ThrowsExactly<KeyLoadException>(() =>
            owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.CommandCapability));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await TenantAdmissionAssertions.UnchangedAsync(fixture, state, cut);
        selected.PrepareDisposal(null, null);
        await selected.DisposeAsync();
        await drain.WaitAsync(new NativeRuntimeTestOptions().CompletionTimeout, token);
        await Assert.That(owner.IsJoined).IsTrue();
        admission.Dispose();
        admission.Dispose();
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        var result = fixture.SubmitIssuedEmbedded(original, token);
        var committed = await TenantAdmissionAssertions.CommittedAsync(fixture, original);
        var expectedToken = new CommitToken(fixture.Store.Identity.Incarnation,
            fixture.Partition.AtomicPartitionId, fixture.Database.LastApplied);
        await TenantAdmissionAssertions.ReceiptAsync(result, committed, "a1", expectedToken);
        await TenantAdmissionAssertions.RecordAsync(fixture.Database, committed, fixture.Partition, "a1");
        var results = new List<Result<object>> { result };
        await TenantAdmissionCold.ReplayAsync(fixture.Database, fixture.Store, fixture.SubmitIssued,
            [committed], results, fixture.Partition, token);
        fixture.JoinOwnedResources();
        await TenantAdmissionCold.RunAsync(fixture, [committed], results, token);
    }
}
