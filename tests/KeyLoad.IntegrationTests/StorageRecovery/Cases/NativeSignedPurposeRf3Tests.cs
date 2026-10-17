namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

/// <summary>Genuine foreground signed request refuses only its unsupported purpose before native routing.</summary>
[NotInParallel]
internal sealed class NativeSignedPurposeRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ForegroundSdkOrOfficialMcpSignedPurposeRefusesThenExactRepairReplayAndTwoColdCuts(bool official)
        => NativeCapabilityOmissionRf3Trial.RunPurposeAsync(official, TestContext.Current!.Execution.CancellationToken);
}
