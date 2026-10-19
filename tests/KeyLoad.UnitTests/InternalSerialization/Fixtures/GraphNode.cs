namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(GraphFixtures.NodeAlias)]
internal sealed class GraphNode
{
    [global::Orleans.Id(1)]
    public GraphNode? Next
    {
        get; set;
    }
}
