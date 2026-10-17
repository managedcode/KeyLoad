namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentCommandPrincipalColdReplayTests
{
    [Test]
    public Task TwoPersistedPrincipalsKeepIndependentNativeReceiptsThroughConflictsAndTwoColdReopens()
        => DocumentPatchAtomicityOwner.RunAsync(DocumentCommandPrincipalColdReplayTrial.RunAsync);
}
