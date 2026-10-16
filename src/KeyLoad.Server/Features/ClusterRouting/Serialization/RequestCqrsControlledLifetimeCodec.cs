using System.Text.Json;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeCodec
{
    private const int MaximumJsonDepth = 4;

    internal static byte[] WritePlan(RequestCqrsControlledLifetimePlan value)
    {
        RequirePlan(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, RequestCqrsControlledLifetimeJsonContext.Default.RequestCqrsControlledLifetimePlan);
    }
    internal static RequestCqrsControlledLifetimePlan ReadPlan(ReadOnlySpan<byte> bytes)
    {
        Unique(bytes);
        var value = JsonSerializer.Deserialize(bytes, RequestCqrsControlledLifetimeJsonContext.Default.RequestCqrsControlledLifetimePlan)
            ?? throw new InvalidOperationException(Invalid);
        RequirePlan(value);
        return value;
    }
    internal static byte[] WriteWitness(RequestCqrsControlledLifetimeStepWitness value)
    {
        RequireWitness(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, RequestCqrsControlledLifetimeJsonContext.Default.RequestCqrsControlledLifetimeStepWitness);
    }
    internal static RequestCqrsControlledLifetimeStepWitness ReadWitness(ReadOnlySpan<byte> bytes)
    {
        Unique(bytes);
        var value = JsonSerializer.Deserialize(bytes, RequestCqrsControlledLifetimeJsonContext.Default.RequestCqrsControlledLifetimeStepWitness)
            ?? throw new InvalidOperationException(Invalid);
        RequireWitness(value);
        return value;
    }
    internal static byte[] WriteAck(RequestCqrsControlledLifetimeStepAck value)
    {
        RequireAck(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, RequestCqrsControlledLifetimeJsonContext.Default.RequestCqrsControlledLifetimeStepAck);
    }
    internal static RequestCqrsControlledLifetimeStepAck ReadAck(ReadOnlySpan<byte> bytes)
    {
        Unique(bytes);
        var value = JsonSerializer.Deserialize(bytes, RequestCqrsControlledLifetimeJsonContext.Default.RequestCqrsControlledLifetimeStepAck)
            ?? throw new InvalidOperationException(Invalid);
        RequireAck(value);
        return value;
    }
    internal static void RequirePlan(RequestCqrsControlledLifetimePlan value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != PlanKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.CommandId == Guid.Empty || !value.PrincipalId.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal)
            || value.Variation is not (ControlledLifetimeReplayVariation.Substituted or ControlledLifetimeReplayVariation.Foreign)
            || (value.Variation == ControlledLifetimeReplayVariation.Foreign
                ? value.SourceArmId is null || value.SourceArmId == Guid.Empty || value.SourceArmId == value.ArmId
                : value.SourceArmId is not null))
        {
            throw new InvalidOperationException(Invalid);
        }
    }
    internal static void RequireWitness(RequestCqrsControlledLifetimeStepWitness value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != StepKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.RequestId == Guid.Empty || value.CommandId == Guid.Empty || value.ConnectionId == Guid.Empty
            || value.EffectCommandId == Guid.Empty || value.Step is < BaselineStep or > LastNegativeStep
            || value.CarrierBytes <= EmptyBytes || value.CarrierParts <= EmptyBytes || !Digest(value.CarrierSha256)
            || value.Step == BaselineStep && (value.Error is not null || value.RequestBytes != EmptyBytes || value.ReplyBytes != EmptyBytes
                || value.Variation != ControlledLifetimeReplayVariation.CaptureOriginal || value.RequestParts != EmptyBytes
                || value.ReplyParts != EmptyBytes || !string.IsNullOrEmpty(value.RequestSha256) || !string.IsNullOrEmpty(value.ReplySha256))
            || value.Step != BaselineStep && (value.Error is null || value.RequestBytes <= EmptyBytes || value.ReplyBytes <= EmptyBytes
                || value.RequestParts <= EmptyBytes || value.ReplyParts <= EmptyBytes || !Digest(value.RequestSha256) || !Digest(value.ReplySha256)))
        {
            throw new InvalidOperationException(Invalid);
        }
    }
    private static void RequireAck(RequestCqrsControlledLifetimeStepAck value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != AckKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.RequestId == Guid.Empty || value.Step is < BaselineStep or > LastNegativeStep)
        {
            throw new InvalidOperationException(Invalid);
        }
    }
    private static bool Digest(string value) => value is { Length: DigestCharacters } && value.All(char.IsAsciiHexDigitLower);
    private static void Unique(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        RequireUnique(document.RootElement);
    }
    private static void RequireUnique(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidOperationException(Invalid);
            }
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                RequireUnique(property.Value);
            }
        }
    }
}
