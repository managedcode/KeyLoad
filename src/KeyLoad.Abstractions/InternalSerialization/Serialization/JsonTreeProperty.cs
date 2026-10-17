namespace KeyLoad.Features.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(JsonSurrogateAliases.Property)]
internal sealed class JsonTreeProperty
{
    [Orleans.Id(0)]
    public string Name { get; init; } = null!;

    [Orleans.Id(1)]
    public JsonTreeNode Value { get; init; } = null!;
}
