using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(JsonSurrogateAliases.Node)]
internal sealed class JsonTreeNode
{
    [Orleans.Id(0)]
    public JsonValueKind Kind
    {
        get; init;
    }

    [Orleans.Id(1)]
    public string? Text
    {
        get; init;
    }

    [Orleans.Id(2)]
    public JsonTreeProperty[]? Properties
    {
        get; init;
    }

    [Orleans.Id(3)]
    public JsonTreeNode[]? Items
    {
        get; init;
    }
}
