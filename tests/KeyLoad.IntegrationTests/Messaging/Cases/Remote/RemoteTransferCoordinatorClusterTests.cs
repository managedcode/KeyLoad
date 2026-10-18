namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferCoordinatorClusterTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task NativeCoordinatorUsesActualReceiptAcrossColdAndKnownQuotaFailureRequiresFreshPublicRepair(bool fullTarget)
        => RemoteTransferCoordinatorClusterTrial.RunAsync(fullTarget);
}
