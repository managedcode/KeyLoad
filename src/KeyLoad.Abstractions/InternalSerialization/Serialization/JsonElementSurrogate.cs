namespace KeyLoad.Features.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(JsonSurrogateAliases.Element)]
internal readonly struct JsonElementSurrogate
{
    [Orleans.Id(0)]
    public JsonTreeNode? Root
    {
        get; init;
    }
}
