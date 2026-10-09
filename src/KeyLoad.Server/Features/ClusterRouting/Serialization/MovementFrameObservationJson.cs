using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class MovementFrameObservationJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IOptions<RequestProbeExecutionOptions> options;
    private readonly RequestCqrsProbeJson shape;
    private readonly MovementFrameObservationJsonContext context;
    internal MovementFrameObservationJson(IOptions<RequestProbeExecutionOptions> options)
    {
        options.Value.Validate();
        this.options = options;
        shape = new(options);
        context = new(new JsonSerializerOptions
        {
            MaxDepth = options.Value.MaximumJsonDepth,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        });
    }

    internal MovementFrameObservationOwner ReadOwner(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, MovementFrameObservationFields.Owner, context.MovementFrameObservationOwner);
        RequireCommon(value.Version, value.Kind, MovementFrameObservationProtocol.OwnerKind, value.SessionId);
        RequireText(value.Voter);
        return value;
    }

    internal MovementFrameObservationSelection ReadSelection(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, MovementFrameObservationFields.Selection, context.MovementFrameObservationSelection);
        RequireCommon(value.Version, value.Kind, MovementFrameObservationProtocol.SelectionKind, value.SessionId);
        if (value.SelectionId == Guid.Empty || value.MoveId == Guid.Empty
            || value.PhysicalShardId == Guid.Empty || value.Incarnation == Guid.Empty
            || !value.OperatorPrincipalId.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal))
        { throw Invalid(); }
        RequireText(value.OperatorPrincipalId);
        return value;
    }

    internal MovementFrameObservationRecord ReadObservation(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, MovementFrameObservationFields.Observation, context.MovementFrameObservationRecord);
        RequireCommon(value.Version, value.Kind, MovementFrameObservationProtocol.ObservationKind, value.SessionId);
        if (value.SelectionId == Guid.Empty || value.MoveId == Guid.Empty
            || value.PhysicalShardId == Guid.Empty || value.Incarnation == Guid.Empty
            || value.EffectCommandId == Guid.Empty || value.OriginalRequestNonce == Guid.Empty
            || value.OriginalExpiresAt == default || value.EntryIndex <= MovementFrameObservationFields.AbsentIndex
            || value.EntryTerm <= MovementFrameObservationFields.AbsentTerm || value.MaximumFrameBytes <= MovementFrameObservationFields.AbsentCap
            || value.RejectedPrefixBytes != checked((long)value.MaximumFrameBytes + MovementFrameObservationProtocol.PrefixStep)
            || !value.OperatorPrincipalId.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal))
        { throw Invalid(); }
        RequireText(value.OperatorPrincipalId);
        RequireText(value.ReceiverPrincipalId);
        RequireText(value.Voter);
        return value;
    }

    internal byte[] WriteOwner(MovementFrameObservationOwner value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, context.MovementFrameObservationOwner);
        _ = ReadOwner(bytes);
        return bytes;
    }

    internal byte[] WriteSelection(MovementFrameObservationSelection value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, context.MovementFrameObservationSelection);
        _ = ReadSelection(bytes);
        return bytes;
    }

    internal byte[] WriteObservation(MovementFrameObservationRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, context.MovementFrameObservationRecord);
        _ = ReadObservation(bytes);
        return bytes;
    }

    private static void RequireCommon(int version, string kind, string expectedKind, string session)
    {
        if (version != MovementFrameObservationProtocol.Version || kind != expectedKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(session))
        { throw Invalid(); }
    }

    private void RequireText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        { throw Invalid(); }
        try
        {
            if (StrictUtf8.GetByteCount(value) > options.Value.MaximumPrincipalBytes)
            { throw Invalid(); }
        }
        catch (EncoderFallbackException)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(MovementFrameObservationProtocol.Invalid);
}
