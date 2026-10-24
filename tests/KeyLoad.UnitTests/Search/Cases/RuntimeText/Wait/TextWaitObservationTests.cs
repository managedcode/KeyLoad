namespace KeyLoad.UnitTests.Search;

internal sealed class TextWaitObservationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalMinimumRefusalPrecedesTypedObservationThenLiteralPagesRemainHealthy(bool future)
        => TextWaitObservationTrial.RunAsync(future, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CurrentFieldUseRefusalPrecedesObservationThenPersistedGrantPublishesLiteralHealthy()
        => TextWaitObservationTrial.RunAsync(null, TestContext.Current!.Execution.CancellationToken);
}
