namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextBm25EligibilityTests
{
    [Test]
    public Task MultiDocumentCapturedPagesInvalidateRetainedHeadsAfterOutboxSchemaAndPolicyChanges()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(TextBm25EligibilityTrial.RunAsync);
}
