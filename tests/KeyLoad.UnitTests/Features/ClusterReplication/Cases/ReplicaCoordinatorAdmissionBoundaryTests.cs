using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaCoordinatorAdmissionBoundaryTests
{
    private const string Principal = "coordinator-admission-root";
    private const string Credential = "coordinator-admission-private-unit-credential";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreAdmissionRefusalRetainsExactCutAndDoesNotBecomeUnknown(bool cancelBeforeAdmission)
    {
        await using var fixture = new ReplicaLifecycleFixture();
        fixture.Database.Bootstrap(new(Principal, "system", [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(Principal, Principal, Credential));
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
        await using var coordinator = new ClusterCoordinator(fixture.Consensus, fixture.Database, governor,
            TimeProvider.System, ReplicaExecutionTestOptions.Execution(), UnitAdmissionOptions.Inbox());
        var id = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(false);
        var operation = fixture.Database.CreateNativeOperation(OperationKind.SetDispatch, id, Principal,
            TimeProvider.System.GetUtcNow(), payload);
        var original = ReplicaCoordinatorAdmissionCut.Read(fixture);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        if (cancelBeforeAdmission)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => coordinator.SubmitVerifiedAsync(operation, cancellation.Token));
        }
        else
        {
            var refusal = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => coordinator.SubmitNativeAsync(
                OperationKind.SetDispatch, id, Principal, payload, cancellation.Token));
            await Assert.That(refusal!.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(refusal.Message).IsEqualTo(ReplicaProtocol.NoLeader);
        }
        await original.RequireAsync(ReplicaCoordinatorAdmissionCut.Read(fixture));
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
        var following = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => coordinator.SubmitVerifiedAsync(
            operation, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(following!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(following.Message).IsEqualTo(ReplicaProtocol.NoLeader);
        await original.RequireAsync(ReplicaCoordinatorAdmissionCut.Read(fixture));
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }
}
