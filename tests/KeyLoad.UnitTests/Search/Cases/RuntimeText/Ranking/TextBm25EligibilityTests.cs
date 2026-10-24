namespace KeyLoad.UnitTests.Search;

internal sealed class TextBm25EligibilityTests
{
    [Test]
    public Task MultiDocumentCapturedPagesInvalidateRetainedHeadsAfterOutboxSchemaAndPolicyChanges()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(TextBm25EligibilityTrial.RunAsync);
}
