using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Builds owned SDK argument dictionaries from exact JSON samples.</summary>
internal static class McpGatewayProtocolInputs
{
    internal static Dictionary<string, JsonElement> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        { throw new InvalidOperationException("The test input must be an object."); }
        return document.RootElement.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
