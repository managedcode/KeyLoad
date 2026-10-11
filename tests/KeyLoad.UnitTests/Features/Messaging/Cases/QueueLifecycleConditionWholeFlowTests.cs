namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLifecycleConditionWholeFlowTests
{
    [Test]
    [Arguments(QueueLifecycleConditionFault.MetadataIdentity)]
    [Arguments(QueueLifecycleConditionFault.BodyIdentity)]
    public Task AcMsg088LifecycleForeignOwnedIdentityRefusesColdThenExactRepairReplayAndHealthyWorkPersist(
        QueueLifecycleConditionFault fault)
        => QueueLifecycleConditionTrial.Corruption(fault);

    [Test]
    [Arguments(QueueLifecycleConditionRefusal.Version)]
    [Arguments(QueueLifecycleConditionRefusal.Generation)]
    [Arguments(QueueLifecycleConditionRefusal.Missing)]
    [Arguments(QueueLifecycleConditionRefusal.Cancelled)]
    public Task AcMsg088LifecycleInvalidConditionMissingAndTerminalOriginalFailureReplaysAcrossColdBeforeHealthyWork(
        QueueLifecycleConditionRefusal kind)
        => QueueLifecycleConditionTrial.Refusal(kind);
}
