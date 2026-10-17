namespace KeyLoad.IntegrationTests.Features.Messaging;

[NotInParallel]
internal sealed class DueFaultRf3LeaderAndColdRestartTests
{
    [Test]
    public Task AcDue003AutonomousOutcomesSurviveLeaderRejoinAndCurrentColdRestart()
        => DueFaultRf3Run.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
}
