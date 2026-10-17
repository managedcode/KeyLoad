namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlCompositionParameterWholeFlowTests
{
    [Test]
    public Task OrdinalTypedCompositionParametersRefuseWithoutEffectsThenForwardReverseReplayAndTwoColdOwners()
        => SqlCompositionParameterFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
