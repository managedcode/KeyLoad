namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class JournalOriginalFailureTests
{
    [Test]
    public Task OriginalClosedJournalCausePoisonsThenSameRootRecoversAndRemainsHealthyAcrossTwoColdOpens()
        => JournalOriginalFailureFlow.RunAsync();
}
