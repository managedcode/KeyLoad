using KeyLoad.Core;

namespace KeyLoad.UnitTests;

public sealed class CommandAdmissionTests
{
    private static CommandAdmissionGovernor.Lease Reserve(CommandAdmissionGovernor governor, OperationKind kind, PrincipalRecord principal,
        int bytes, int characters, CancellationToken? cancellationToken = null)
        => governor.Reserve(kind, principal, bytes, characters, cancellationToken ?? TestContext.Current.CancellationToken);
    private static PrincipalRecord Principal(string id = "p", string tenant = "tenant") => new(id, tenant, [], []);
    [Fact]
    public void BytesAndScopesAreReservedAtomicallyAndReleasedExactlyOnce()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxRetainedBytes = 4_497 });
        var first = Reserve(governor, OperationKind.Batch, Principal(), 100, 100);
        Assert.Equal(4_496, governor.Snapshot().RetainedBytes);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Batch, Principal("another"), 0, 0)).Code);
        Assert.Equal(1, governor.Snapshot().Commands);
        first.Dispose(); first.Dispose(); Assert.Equal(new(0, 0, 0, 0, 0, 0), governor.Snapshot());
        using var admitted = Reserve(governor, OperationKind.Batch, Principal("another"), 100, 100);
    }
    [Fact]
    public void TenantAndPrincipalCapsCannotBeEvadedWithMoreApiKeys()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxPrincipalCommands = 1, MaxTenantCommands = 2 });
        using var first = Reserve(governor, OperationKind.Batch, Principal(), 0, 0);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Batch, Principal(), 0, 0)).Code);
        using var second = Reserve(governor, OperationKind.Batch, Principal("second"), 0, 0);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Batch, Principal("third"), 0, 0)).Code);
        using var otherTenant = Reserve(governor, OperationKind.Batch, Principal("fourth", "other"), 0, 0);
        Assert.Equal(3, governor.Snapshot().Commands);
    }
    [Fact]
    public void FullDataAdmissionPreservesBoundedControlCapacity()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxCommands = 1, ReservedControlCommands = 2, MaxControlPayloadBytes = 16 });
        using var data = Reserve(governor, OperationKind.Batch, Principal(), 0, 0);
        using var acknowledgement = Reserve(governor, OperationKind.Delivery, Principal(), 16, 16);
        using var membership = Reserve(governor, OperationKind.Membership, Principal("root"), 0, 0);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() =>
            Reserve(governor, OperationKind.SubscriptionDelivery, Principal(), 0, 0)).Code);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() =>
            Reserve(governor, OperationKind.Delivery, Principal(), 17, 17)).Code);
        Assert.Equal(1, governor.Snapshot().Commands); Assert.Equal(2, governor.Snapshot().ControlCommands);
    }
    [Fact]
    public void CancellationBeforeAdmissionDoesNotConsumeCapacity()
    {
        var governor = new CommandAdmissionGovernor(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Reserve(governor, OperationKind.Batch, Principal(), 0, 0, cancellation.Token));
        Assert.Equal(new(0, 0, 0, 0, 0, 0), governor.Snapshot());
    }
    [Fact]
    public async Task ConcurrentLoadCannotExceedTheNodeCeilingAndReleasedScopesDoNotAccumulate()
    {
        var governor = new CommandAdmissionGovernor(new() { MaxCommands = 8 });
        using var held = new CountdownEvent(8); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            using var lease = Reserve(governor, OperationKind.Batch, Principal("p-" + i, "t-" + i), 1, 1);
            held.Signal(); await release.Task;
        })).ToArray();
        try
        {
            Assert.True(held.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); Assert.Equal(8, governor.Snapshot().Commands);
            var rejected = await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
                Assert.Throws<KeyLoadException>(() => Reserve(governor, OperationKind.Batch, Principal("rejected-" + i), 0, 0)).Code)));
            Assert.All(rejected, code => Assert.Equal(ErrorCode.ResourceExhausted, code));
            Assert.Equal(8, governor.Snapshot().ActiveTenantScopes); Assert.Equal(8, governor.Snapshot().ActivePrincipalScopes);
        }
        finally { release.TrySetResult(); await Task.WhenAll(admitted); }
        Assert.Equal(new(0, 0, 0, 0, 0, 0), governor.Snapshot());
    }
}
