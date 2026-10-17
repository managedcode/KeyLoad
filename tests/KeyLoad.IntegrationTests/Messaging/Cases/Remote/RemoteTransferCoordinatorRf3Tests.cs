namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferCoordinatorRf3Tests
{
    [Test, Arguments(false), Arguments(true)]
    public Task NativeCoordinatorUsesActualReceiptAcrossColdAndKnownQuotaFailureRequiresFreshPublicRepair(bool fullTarget)
        => RemoteTransferCoordinatorRf3Trial.RunAsync(fullTarget);
}
