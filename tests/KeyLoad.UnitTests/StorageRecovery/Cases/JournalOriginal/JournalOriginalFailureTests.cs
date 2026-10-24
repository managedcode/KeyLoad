namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class JournalOriginalFailureTests
{
    [Test]
    public Task OriginalClosedJournalCausePoisonsThenSameRootRecoversAndRemainsHealthyAcrossTwoColdOpens()
        => JournalOriginalFailureFlow.RunAsync();
}
