namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Supporting native Kestrel proof; Linux Docker qualification remains separately mandatory.</summary>
[NotInParallel]
internal sealed class RemoteTransferNativeWireTests
{
    [Test]
    public Task OriginalMacMalformedEnvelopeHasNoEffectsThenSameTransferCompletesTwoColdOwnersAndFreshPublicContinuation()
        => RemoteTransferNativeWireTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
