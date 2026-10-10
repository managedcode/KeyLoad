namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Supporting original signed nonce refusal; delivered Linux Docker gates remain mandatory.</summary>
[NotInParallel]
internal sealed class RemoteTransferOriginalNonceTests
{
    [Test]
    public Task OriginalSignedPacketCommitsThenDuplicateNonceRefusesAndSameIdReconcilesTwoColdOwners()
        => RemoteTransferOriginalNonceTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
