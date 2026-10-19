namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferCoordinatorClusterTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task CoordinatorUsesActualReceiptAcrossColdAndKnownQuotaFailureRequiresFreshPublicRepair(bool fullTarget)
        => RemoteTransferCoordinatorClusterTrial.RunAsync(fullTarget);
}
