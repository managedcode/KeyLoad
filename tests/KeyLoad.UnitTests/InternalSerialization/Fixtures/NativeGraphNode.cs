namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeGraphFixtures.NodeAlias)]
internal sealed class NativeGraphNode
{
    [global::Orleans.Id(1)]
    public NativeGraphNode? Next
    {
        get; set;
    }
}
