using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionRevokedGrantTests
{
    private const int InitialPolicyEpoch = 1;
    private const int RevokedPolicyEpoch = 2;

    [Test]
    public async Task AcEventRetention002RevokedManagerCannotReplayPrivilegedReceiptOrAlterItsCut()
    {
        using var fixture = new TopicRetentionFixture();
        var manager = fixture.Owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new("manager", "tenant", [new("database", "topic", Capability.SchemaManage | Capability.TopicsRead)], [])))
            .Get<PrincipalRecord>();
        await Assert.That(manager.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        fixture.ReleasePins();
        var command = fixture.Purge();
        var receipt = fixture.Submit(command, "manager").Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        var revoked = fixture.Owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            manager with { Grants = [new("database", "topic", Capability.TopicsRead)], PolicyEpoch = RevokedPolicyEpoch }))
            .Get<PrincipalRecord>();
        await Assert.That(revoked.PolicyEpoch).IsEqualTo(RevokedPolicyEpoch);
        var cut = fixture.Store.Position;
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var denied = fixture.Submit(command, "manager");
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        var second = fixture.Submit(command, "manager");
        await Assert.That(JsonDefaults.Serialize(second).AsSpan().SequenceEqual(JsonDefaults.Serialize(denied))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await TopicRetentionAssertions.RetainedTail(fixture);
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }
}
