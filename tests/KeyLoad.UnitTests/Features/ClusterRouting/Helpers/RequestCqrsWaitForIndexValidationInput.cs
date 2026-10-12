using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsWaitForIndexValidationInput
{
    private const string FixturePartitionKey = "partition";

    internal RequestProbeExecutionOptions Settings { get; } = new();
    internal RequestCqrsProbeJson Json { get; }
    internal RequestCqrsWaitForIndexReadCutWitness Value { get; }

    internal RequestCqrsWaitForIndexValidationInput()
    {
        Json = new(Options.Create(Settings));
        var partition = new RequestCqrsProbePartitionRecord("tenant", "database", "domain", FixturePartitionKey);
        var incarnation = Guid.NewGuid();
        Value = new(RequestCqrsProbeProtocol.Version, RequestCqrsWaitForIndexReadCutInventory.Kind,
            Guid.NewGuid().ToString("N"), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(),
            "c1-probe-wait-validator-input", "n1", "127.0.0.1:11111@1", new('a', 64),
            ActivationId.NewId().ToParsableString(), Guid.NewGuid(), incarnation, 1, incarnation,
            partition.ToPartition().AtomicPartitionId, 17, 1, 4, 1, 1, new('b', 64), partition);
    }

    internal RequestCqrsProbeArmRecord Arm => new(RequestCqrsProbeProtocol.Version,
        RequestCqrsProbeProtocol.ArmKind, Value.SessionId, Value.ArmId, Value.PrincipalId, Value.CommandId,
        GrainReadKind.WaitForIndex, RequestCqrsProbePhase.RequestStarted, RequestCqrsProbeAction.Hold);

    internal RequestCqrsProbeMarkerRecord[] Markers =>
    [
        Marker(RequestCqrsProbePhase.RequestStarted, RequestCqrsProbeOutcome.Observed),
        Marker(RequestCqrsProbePhase.RequestStarted, RequestCqrsProbeOutcome.Released),
        Marker(RequestCqrsProbePhase.WaitForIndexReadCutCaptured, RequestCqrsProbeOutcome.Observed)
    ];

    private RequestCqrsProbeMarkerRecord Marker(RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.MarkerKind,
            Value.SessionId, Value.ArmId, Value.RequestId, Value.CommandId, phase, outcome, Value.Voter, Value.SiloAddress);

    internal byte[] Write() => JsonSerializer.SerializeToUtf8Bytes(Value, Json.WaitCutTypeInfo);

    internal RequestCqrsWaitForIndexReadCutWitness Read(byte[] bytes)
    {
        var value = Json.Read(bytes, RequestCqrsWaitForIndexReadCutInventory.Fields, Json.WaitCutTypeInfo);
        RequestCqrsWaitForIndexReadCutInventory.Require(value);
        return value;
    }

    internal byte[] Change(Action<JsonObject> change)
    {
        var value = JsonNode.Parse(Encoding.UTF8.GetString(Write()))!.AsObject();
        change(value);
        return Encoding.UTF8.GetBytes(value.ToJsonString());
    }

    internal byte[] DuplicateOrEscapeVersion(bool duplicate)
    {
        var original = Encoding.UTF8.GetString(Write());
        var version = "\"Version\":" + RequestCqrsProbeProtocol.Version;
        var replacement = duplicate ? version + "," + version : "\"\\u0056ersion\":" + RequestCqrsProbeProtocol.Version;
        return Encoding.UTF8.GetBytes(original.Replace(version, replacement, StringComparison.Ordinal));
    }

    internal byte[] PadTo(int length)
    {
        var bytes = Write();
        var padded = new byte[length];
        padded.AsSpan().Fill((byte)' ');
        bytes.CopyTo(padded, 0);
        return padded;
    }

    private static readonly JsonSerializerOptions ArmOptions = CreateArmOptions();

    internal RequestCqrsProbeArmRecord ReadArm(RequestCqrsProbeArmRecord value)
        => Json.ReadArm(JsonSerializer.SerializeToUtf8Bytes(value, ArmOptions));

    private static JsonSerializerOptions CreateArmOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter<GrainReadKind>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbePhase>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeAction>(null, false));
        return options;
    }
}
