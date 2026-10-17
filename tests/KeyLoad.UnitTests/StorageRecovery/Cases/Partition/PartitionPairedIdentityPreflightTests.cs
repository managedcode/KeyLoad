using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionPairedIdentityPreflightTests
{
    [Test]
    [Arguments(false, StoreReaderContract.Unspecified)]
    [Arguments(false, PartitionPairedIdentityProtocol.UnknownCapability)]
    [Arguments(true, StoreReaderContract.Unspecified)]
    public Task InvalidCurrentIdentityRejectsBothStoresBeforeTornTailRecoveryThenRepairContinuesCold(
        bool canonical, int capability)
        => PartitionPairedIdentityPreflightTrial.RunAsync(canonical, capability,
            TestContext.Current!.Execution.CancellationToken);
}
