namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextMaintenanceLifetimeNativeTests
{
    [Test]
    public Task ActualNativeTextSessionSurvivesSettledBeginChildThenIndexesLiteralRowsAndJoinsCancellation()
        => TextMaintenanceLifetimeNativeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
