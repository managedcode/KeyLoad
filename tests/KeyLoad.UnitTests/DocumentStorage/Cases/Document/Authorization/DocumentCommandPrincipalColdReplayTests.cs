namespace KeyLoad.UnitTests.DocumentStorage;

internal sealed class DocumentCommandPrincipalColdReplayTests
{
    [Test]
    public Task TwoPersistedPrincipalsKeepIndependentReceiptsThroughConflictsAndTwoColdReopens()
        => DocumentPatchAtomicityOwner.RunAsync(DocumentCommandPrincipalColdReplayTrial.RunAsync);
}
