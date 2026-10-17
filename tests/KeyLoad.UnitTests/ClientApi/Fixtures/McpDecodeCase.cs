using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Owned public JSON and its independent actual-type decoding operations.</summary>
internal sealed record McpDecodeCase(string Name, JsonElement Request, Guid CommandId, string? IdMember,
    Func<JsonElement, object> DeserializePublic, Func<ReadOnlyMemory<byte>, object> DeserializeNative,
    Func<object, byte[]> SerializeNative, Action<object, Stream> SerializeNativeToStream,
    Func<object, JsonElement> SerializePublic)
{
    internal IDictionary<string, JsonElement> Arguments()
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = Request
        };
        if (CommandId != Guid.Empty && IdMember is null)
        {
            arguments.Add(McpCanonicalTestData.CommandKey, JsonSerializer.SerializeToElement(CommandId, JsonDefaults.Options));
        }
        return arguments;
    }
}
