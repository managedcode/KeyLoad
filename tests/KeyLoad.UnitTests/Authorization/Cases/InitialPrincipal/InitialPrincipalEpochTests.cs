namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class InitialPrincipalEpochTests
{
    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(long.MinValue)]
    public Task RefusedInitialEpochThenKeyLifecycleAndScopedEffectsSurviveTwoColdOwners(long epoch)
        => InitialPrincipalEpochFlow.RunAsync(epoch, TestContext.Current!.Execution.CancellationToken);
}
