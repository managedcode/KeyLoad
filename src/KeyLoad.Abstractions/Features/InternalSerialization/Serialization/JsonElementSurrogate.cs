using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class JsonSurrogateAliases
{
    internal const string Element = "keyload.json.element.v1";
    internal const string Node = "keyload.json.node.v1";
    internal const string Property = "keyload.json.property.v1";
}

[Orleans.GenerateSerializer]
[Orleans.Alias(JsonSurrogateAliases.Element)]
internal readonly struct JsonElementSurrogate
{
    [Orleans.Id(0)]
    public JsonTreeNode? Root { get; init; }
}

[Orleans.GenerateSerializer]
[Orleans.Alias(JsonSurrogateAliases.Node)]
internal sealed class JsonTreeNode
{
    [Orleans.Id(0)]
    public JsonValueKind Kind { get; init; }

    [Orleans.Id(1)]
    public string? Text { get; init; }

    [Orleans.Id(2)]
    public JsonTreeProperty[]? Properties { get; init; }

    [Orleans.Id(3)]
    public JsonTreeNode[]? Items { get; init; }
}

[Orleans.GenerateSerializer]
[Orleans.Alias(JsonSurrogateAliases.Property)]
internal sealed class JsonTreeProperty
{
    [Orleans.Id(0)]
    public string Name { get; init; } = null!;

    [Orleans.Id(1)]
    public JsonTreeNode Value { get; init; } = null!;
}
