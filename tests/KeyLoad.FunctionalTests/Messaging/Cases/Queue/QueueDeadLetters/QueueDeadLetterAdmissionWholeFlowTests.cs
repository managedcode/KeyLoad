namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueDeadLetterAdmissionWholeFlowTests
{
    [Test]
    [Arguments(QueueDeadLetterAdmissionFault.MissingCounters)]
    [Arguments(QueueDeadLetterAdmissionFault.NegativeStoredMessages)]
    [Arguments(QueueDeadLetterAdmissionFault.NegativeDeadLetterMessages)]
    [Arguments(QueueDeadLetterAdmissionFault.NegativeNextParkedSequence)]
    [Arguments(QueueDeadLetterAdmissionFault.ExcessiveDeadLetterBytes)]
    [Arguments(QueueDeadLetterAdmissionFault.WitnessWithoutUsage)]
    [Arguments(QueueDeadLetterAdmissionFault.MissingWitnessMetadata)]
    public Task AcMsgDlqAdmission001CounterAndWitnessRefuseThenColdRepairOriginalCancelReplayAndFreshProcessing(
        QueueDeadLetterAdmissionFault fault)
        => QueueDeadLetterAdmissionWholeFlow.Run(fault, TestContext.Current!.Execution.CancellationToken);
}
