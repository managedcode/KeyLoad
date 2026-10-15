namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class JournalOriginalFailureTests
{
    [Test]
    public Task OriginalClosedJournalCausePoisonsThenSameRootRecoversAndRemainsHealthyAcrossTwoColdOpens()
        => JournalOriginalFailureFlow.RunAsync();
}
