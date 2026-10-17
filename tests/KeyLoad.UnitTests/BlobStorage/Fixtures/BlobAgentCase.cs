using System.Text.Json;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>One operation retaining public JSON arguments/bytes and separately encoded native typed payload.</summary>
internal sealed record BlobAgentCase(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind,
    JsonElement Request, ReadOnlyMemory<byte> Payload, ReadOnlyMemory<byte> NativePayload)
{
    internal Dictionary<string, JsonElement> Arguments() => new(StringComparer.Ordinal) { [BlobAgentCases.Request] = Request };
}
