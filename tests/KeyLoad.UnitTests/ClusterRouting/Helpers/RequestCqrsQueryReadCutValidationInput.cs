using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsQueryReadCutValidationInput
{
    private const string FixturePartitionKey = "partition";

    internal RequestProbeExecutionOptions Settings { get; } = new();
    internal RequestCqrsProbeJson Json
    {
        get;
    }
    internal RequestCqrsQueryReadCutWitness Value
    {
        get;
    } = new(
        RequestCqrsProbeProtocol.Version, RequestCqrsQueryReadCutInventory.Kind, Guid.NewGuid().ToString("N"),
        Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "c1-probe-parser-input",
        "n1", "127.0.0.1:11111@1", new('a', 64), ActivationId.NewId().ToParsableString(),
        Guid.NewGuid(), Guid.NewGuid(), 1, 1, 2, 1, 1, new('b', 64),
        new("tenant", "database", "domain", FixturePartitionKey), "bounded-full-scan");

    internal RequestCqrsQueryReadCutValidationInput()
    {
        Json = new(Options.Create(Settings));
    }

    internal byte[] Write() => JsonSerializer.SerializeToUtf8Bytes(Value, Json.QueryCutTypeInfo);

    internal RequestCqrsQueryReadCutWitness Read(byte[] bytes)
    {
        var value = Json.Read(bytes, RequestCqrsQueryReadCutInventory.Fields, Json.QueryCutTypeInfo);
        RequestCqrsQueryReadCutInventory.Require(value);
        return value;
    }

    internal byte[] Change(Action<JsonObject> change)
    {
        var value = JsonNode.Parse(Encoding.UTF8.GetString(Write()))!.AsObject();
        change(value);
        return Encoding.UTF8.GetBytes(value.ToJsonString());
    }

    internal byte[] PadTo(int length)
    {
        var bytes = Write();
        var padded = new byte[length];
        padded.AsSpan().Fill((byte)' ');
        bytes.CopyTo(padded, 0);
        return padded;
    }

    internal byte[] DuplicateOrEscapeVersion(bool duplicate)
    {
        var original = Encoding.UTF8.GetString(Write());
        var version = "\"Version\":" + RequestCqrsProbeProtocol.Version;
        var replacement = duplicate ? version + "," + version : "\"\\u0056ersion\":" + RequestCqrsProbeProtocol.Version;
        return Encoding.UTF8.GetBytes(original.Replace(version, replacement, StringComparison.Ordinal));
    }
}
