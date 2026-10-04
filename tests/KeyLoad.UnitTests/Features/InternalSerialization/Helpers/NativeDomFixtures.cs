using System.Text.Json;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeDomFixtures
{
    internal const string EscapedText = "界λ🙂\"\\\n<&";
    internal const string Number = "1.00e+02";
    internal const string Name = "name";

    internal static JsonTreeNode Null() => new() { Kind = JsonValueKind.Null };

    internal static JsonTreeNode Text(string value) => new() { Kind = JsonValueKind.String, Text = value };

    internal static JsonTreeNode Arrays(int depth)
    {
        var node = Null();
        for (var level = 0; level < depth; level++)
        {
            node = new() { Kind = JsonValueKind.Array, Items = [node] };
        }
        return node;
    }

    internal static JsonElement Convert(JsonTreeNode node)
        => new JsonElementSurrogateConverter().ConvertFromSurrogate(new JsonElementSurrogate { Root = node });
}
