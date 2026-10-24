namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueDeadLetterSelectedAuthorityWholeFlowTests
{
    [Test]
    [Arguments(QueueDeadLetterSelectedOperation.Read, QueueDeadLetterSelectedFault.IndexAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Cancel, QueueDeadLetterSelectedFault.IndexAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Redrive, QueueDeadLetterSelectedFault.IndexAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Read, QueueDeadLetterSelectedFault.MetadataAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Cancel, QueueDeadLetterSelectedFault.MetadataAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Redrive, QueueDeadLetterSelectedFault.MetadataAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Read, QueueDeadLetterSelectedFault.WitnessMetadataAlias)]
    [Arguments(QueueDeadLetterSelectedOperation.Read, QueueDeadLetterSelectedFault.MissingBody)]
    [Arguments(QueueDeadLetterSelectedOperation.Read, QueueDeadLetterSelectedFault.ForeignBody)]
    public Task SelectedDeadLetterAuthorityRefusesThenExactRepairOriginalContinuationAndHealthyInboxSurviveCold(
        QueueDeadLetterSelectedOperation kind, QueueDeadLetterSelectedFault fault)
        => QueueDeadLetterSelectedTrial.RunAsync(kind, fault, TestContext.Current!.Execution.CancellationToken);
}
