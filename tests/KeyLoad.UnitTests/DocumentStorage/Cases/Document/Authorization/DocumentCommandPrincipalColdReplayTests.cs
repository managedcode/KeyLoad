namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentCommandPrincipalColdReplayTests
{
    [Test]
    public Task TwoPersistedPrincipalsKeepIndependentReceiptsThroughConflictsAndTwoColdReopens()
        => DocumentPatchAtomicityOwner.RunAsync(DocumentCommandPrincipalColdReplayTrial.RunAsync);
}
