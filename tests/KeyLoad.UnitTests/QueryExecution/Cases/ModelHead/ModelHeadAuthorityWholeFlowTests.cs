namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class ModelHeadAuthorityWholeFlowTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public Task CorruptHeadCannotMasqueradeAsStaleSourceAndExactRepairRetainsPureModelOperations(int fault)
        => ModelHeadAuthorityFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
