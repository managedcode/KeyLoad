namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLeaseAuthorityWholeFlowTests
{
    [Test]
    [Arguments(QueueLeaseAuthorityOperation.Ack, QueueLeaseAuthorityFault.MissingReference)]
    [Arguments(QueueLeaseAuthorityOperation.Ack, QueueLeaseAuthorityFault.ForeignReference)]
    [Arguments(QueueLeaseAuthorityOperation.Ack, QueueLeaseAuthorityFault.ForeignBodyIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Ack, QueueLeaseAuthorityFault.ForeignMetadataIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Ack, QueueLeaseAuthorityFault.MissingMetadataDeadline)]
    [Arguments(QueueLeaseAuthorityOperation.Nack, QueueLeaseAuthorityFault.MissingReference)]
    [Arguments(QueueLeaseAuthorityOperation.Nack, QueueLeaseAuthorityFault.ForeignReference)]
    [Arguments(QueueLeaseAuthorityOperation.Nack, QueueLeaseAuthorityFault.ForeignBodyIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Nack, QueueLeaseAuthorityFault.ForeignMetadataIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Nack, QueueLeaseAuthorityFault.MissingMetadataDeadline)]
    [Arguments(QueueLeaseAuthorityOperation.Renew, QueueLeaseAuthorityFault.MissingReference)]
    [Arguments(QueueLeaseAuthorityOperation.Renew, QueueLeaseAuthorityFault.ForeignReference)]
    [Arguments(QueueLeaseAuthorityOperation.Renew, QueueLeaseAuthorityFault.ForeignBodyIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Renew, QueueLeaseAuthorityFault.ForeignMetadataIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Renew, QueueLeaseAuthorityFault.MissingMetadataDeadline)]
    [Arguments(QueueLeaseAuthorityOperation.Processing, QueueLeaseAuthorityFault.MissingReference)]
    [Arguments(QueueLeaseAuthorityOperation.Processing, QueueLeaseAuthorityFault.ForeignReference)]
    [Arguments(QueueLeaseAuthorityOperation.Processing, QueueLeaseAuthorityFault.ForeignBodyIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Processing, QueueLeaseAuthorityFault.ForeignMetadataIdentity)]
    [Arguments(QueueLeaseAuthorityOperation.Processing, QueueLeaseAuthorityFault.MissingMetadataDeadline)]
    public Task CurrentLeaseRequiresExactRetainedAuthorityAcrossColdRepairOriginalAndHealthySettlement(
        QueueLeaseAuthorityOperation operation, QueueLeaseAuthorityFault fault)
        => QueueLeaseAuthorityTrial.RunAsync(operation, fault, TestContext.Current!.Execution.CancellationToken);
}
