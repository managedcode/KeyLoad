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

    [Test]
    [Arguments(MessageState.Acked, false)]
    [Arguments(MessageState.Acked, true)]
    [Arguments(MessageState.Cancelled, false)]
    [Arguments(MessageState.Cancelled, true)]
    [Arguments(MessageState.Expired, false)]
    [Arguments(MessageState.Expired, true)]
    public Task AcMsg088TerminalInspectionRefusesRetainedBodyThenExactRepairTwoColdOwnersAndHealthyAck(
        MessageState terminalState, bool foreignBody)
        => QueueTerminalInspectionWholeFlow.RunAsync(terminalState, foreignBody,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(-1)]
    [Arguments(8)]
    public Task AcMsg088UndefinedPersistedStateRefusesInspectionThenExactRepairTwoColdOwnersAndHealthyAck(
        int persistedState)
        => QueueUnknownStateInspectionWholeFlow.RunAsync(persistedState,
            TestContext.Current!.Execution.CancellationToken);
}
