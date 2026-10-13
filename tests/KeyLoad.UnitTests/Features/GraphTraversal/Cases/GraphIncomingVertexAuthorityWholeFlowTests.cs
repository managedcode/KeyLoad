namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingVertexAuthorityWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task IncomingSelectedVertexIdentityRefusesWithoutPartialRowsThenExactRepairAndReceiptsSurviveTwoColdCuts(bool source)
        => GraphIncomingVertexAuthorityFlow.Run(source);
}
