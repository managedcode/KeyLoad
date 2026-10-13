namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextBm25EligibilityTests
{
    [Test]
    public Task MultiDocumentCapturedPagesInvalidateRetainedHeadsAfterOutboxSchemaAndPolicyChanges()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(NativeTextBm25EligibilityTrial.RunAsync);
}
