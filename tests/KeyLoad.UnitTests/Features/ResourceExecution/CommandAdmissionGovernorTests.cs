using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CommandAdmissionGovernorTests
{
    private static CommandAdmissionLease Reserve(CommandAdmissionGovernor governor, OperationKind kind, PrincipalRecord principal,
        int bytes, int characters, CancellationToken? cancellationToken = null)
        => governor.Reserve(kind, principal, bytes, characters, cancellationToken ?? TestContext.Current!.Execution.CancellationToken);
    private static PrincipalRecord Principal(string id = "p", string tenant = "tenant") => new(id, tenant, [], []);
    [Test]
    public async Task BytesAndScopesAreReservedAtomicallyAndReleasedExactlyOnce()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxRetainedBytes = 4_497 });
        var first = Reserve(governor, OperationKind.Batch, Principal(), 100, 100);
        await Assert.That(governor.Snapshot().RetainedBytes).IsEqualTo(4_496);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Batch, Principal("another"), 0, 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        first.Dispose();
        first.Dispose();
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
        using var admitted = Reserve(governor, OperationKind.Batch, Principal("another"), 100, 100);
    }
    [Test]
    public async Task TenantAndPrincipalCapsCannotBeEvadedWithMoreApiKeys()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxPrincipalCommands = 1, MaxTenantCommands = 2 });
        using var first = Reserve(governor, OperationKind.Batch, Principal(), 0, 0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Batch, Principal(), 0, 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using var second = Reserve(governor, OperationKind.Batch, Principal("second"), 0, 0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Batch, Principal("third"), 0, 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using var otherTenant = Reserve(governor, OperationKind.Batch, Principal("fourth", "other"), 0, 0);
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(3);
    }
    [Test]
    public async Task FullDataAdmissionPreservesBoundedControlCapacity()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxCommands = 1, ReservedControlCommands = 2, MaxControlPayloadBytes = 16 });
        using var data = Reserve(governor, OperationKind.Batch, Principal(), 0, 0);
        using var acknowledgement = Reserve(governor, OperationKind.Delivery, Principal(), 16, 16);
        using var membership = Reserve(governor, OperationKind.Membership, Principal("root"), 0, 0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            Reserve(governor, OperationKind.SubscriptionDelivery, Principal(), 0, 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Delivery, Principal(), 17, 17)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(2);
    }
    [Test]
    public async Task CancellationBeforeAdmissionDoesNotConsumeCapacity()
    {
        var governor = new CommandAdmissionGovernor();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => Reserve(governor, OperationKind.Batch, Principal(), 0, 0, cancellation.Token));
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }
    [Test]
    public async Task ConcurrentLoadCannotExceedTheNodeCeilingAndReleasedScopesDoNotAccumulate()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxCommands = 8 });
        using var held = new CountdownEvent(8);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            using var lease = Reserve(governor, OperationKind.Batch, Principal("p-" + i, "t-" + i), 1, 1);
            held.Signal();
            await release.Task;
        })).ToArray();
        try
        {
            await Assert.That(held.Wait(TimeSpan.FromSeconds(10), TestContext.Current!.Execution.CancellationToken)).IsTrue();
            await Assert.That(governor.Snapshot().Commands).IsEqualTo(8);
            var rejected = await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
                Assert.ThrowsExactly<KeyLoadException>(() => Reserve(governor, OperationKind.Batch, Principal("rejected-" + i), 0, 0)).Code)));
            foreach (var code in rejected)
            {
                await Assert.That(code).IsEqualTo(ErrorCode.ResourceExhausted);
            }

            await Assert.That(governor.Snapshot().ActiveTenantScopes).IsEqualTo(8);
            await Assert.That(governor.Snapshot().ActivePrincipalScopes).IsEqualTo(8);
        }
        finally { release.TrySetResult(); await Task.WhenAll(admitted); }
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }
}
