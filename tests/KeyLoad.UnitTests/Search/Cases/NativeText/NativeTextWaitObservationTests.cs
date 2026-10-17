namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextWaitObservationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalMinimumRefusalPrecedesTypedNativeObservationThenLiteralPagesRemainHealthy(bool future)
        => NativeTextWaitObservationTrial.RunAsync(future, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CurrentFieldUseRefusalPrecedesObservationThenPersistedGrantPublishesLiteralHealthy()
        => NativeTextWaitObservationTrial.RunAsync(null, TestContext.Current!.Execution.CancellationToken);
}
