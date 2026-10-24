namespace KeyLoad.UnitTests.Authorization;

internal sealed class ModelRootPolicyTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task WholeEventOrQueuePayloadOrHeaderPolicyOmitsRootWhileLiteralEmptyChildRemainsDistinct(
        bool queue, bool headerRoot)
        => ModelRootPolicyFlow.RunAsync(queue, headerRoot, TestContext.Current!.Execution.CancellationToken);
}
