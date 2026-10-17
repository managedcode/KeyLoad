namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

/// <summary>Actual foreground credential child receives producer-signed physically omitted Id7 before command effects.</summary>
[NotInParallel]
internal sealed class NativeCapabilityOmissionRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ForegroundSdkOrOfficialMcpOmittedCapabilityRefusesThenSameOwnerRepairAndColdReplay(bool official)
        => NativeCapabilityOmissionRf3Trial.RunAsync(official, TestContext.Current!.Execution.CancellationToken);
}
