using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeJson
{
    private const string InvalidRecord = "The private request probe record is invalid.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IOptions<RequestProbeExecutionOptions> executionOptions;
    private readonly RequestCqrsProbeJsonContext Context;

    internal RequestCqrsProbeJson(IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        this.executionOptions = executionOptions;
        Context = CreateContext();
    }

    internal RequestCqrsProbeOwnerRecord ReadOwner(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Owner, Context.RequestCqrsProbeOwnerRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.OwnerKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || string.IsNullOrWhiteSpace(value.Voter))
        { throw Invalid(); }
        return value;
    }

    internal RequestCqrsProbeArmRecord ReadArm(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Arm, Context.RequestCqrsProbeArmRecord);
        var read = value.ReadKind.HasValue;
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.ArmKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || !ValidPrincipal(value.PrincipalId)
            || !Enum.IsDefined(value.Action) || !Enum.IsDefined(value.Phase)
            || value.Phase == RequestCqrsProbePhase.ProducerDisposed
            || read != (value.CommandId == Guid.Empty) || read && !Enum.IsDefined(value.ReadKind!.Value))
        { throw Invalid(); }
        return value;
    }

    internal RequestCqrsProbeReleaseRecord ReadRelease(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Release, Context.RequestCqrsProbeReleaseRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.ReleaseKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty)
        { throw Invalid(); }
        return value;
    }

    internal RequestCqrsProbeMarkerRecord ReadMarker(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Marker, Context.RequestCqrsProbeMarkerRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.MarkerKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty
            || !Enum.IsDefined(value.Phase) || !Enum.IsDefined(value.Outcome)
            || string.IsNullOrWhiteSpace(value.Voter) || string.IsNullOrWhiteSpace(value.SiloAddress))
        { throw Invalid(); }
        return value;
    }

    internal RequestCqrsProbeDiscoveryRecord ReadDiscovery(ReadOnlySpan<byte> bytes)
    {
        const int ApplicationRpcVersionValidationBoundary = 0;
        const int PeerEnvelopeVersionValidationBoundary = 0;

        var value = Read(bytes, RequestCqrsProbeRecordFields.Discovery, Context.RequestCqrsProbeDiscoveryRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version
            || value.Kind != RequestCqrsProbeProtocol.DiscoveryKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || string.IsNullOrWhiteSpace(value.ObserverVoterId)
            || string.IsNullOrWhiteSpace(value.PeerVoterId)
            || value.ObserverVoterId == value.PeerVoterId
            || value.ApplicationRpcVersion < ApplicationRpcVersionValidationBoundary || value.PeerEnvelopeVersion < PeerEnvelopeVersionValidationBoundary
            || value.ProtocolCompatible)
        { throw Invalid(); }
        return value;
    }

    internal byte[] WriteDiscovery(RequestCqrsProbeDiscoveryRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Context.RequestCqrsProbeDiscoveryRecord);
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        return bytes;
    }

    internal byte[] WriteMarker(RequestCqrsProbeMarkerRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Context.RequestCqrsProbeMarkerRecord);
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        return bytes;
    }

    private RequestCqrsProbeJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = executionOptions.Value.MaximumJsonDepth,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbePhase>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeAction>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeOutcome>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<KeyLoad.Orleans.GrainReadKind>(null, false));
        return new RequestCqrsProbeJsonContext(options);
    }

    private void ValidateShape(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> fields)
    {
        const int IndexOfValidationBoundary = 0;

        if (bytes.IsEmpty || bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        try
        { _ = StrictUtf8.GetCharCount(bytes); }
        catch (DecoderFallbackException)
        { throw Invalid(); }
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = executionOptions.Value.MaximumJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        { throw Invalid(); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
            { throw Invalid(); }
            var name = reader.GetString();
            if (name is null || fields.IndexOf(name) < IndexOfValidationBoundary || !seen.Add(name)
                || !reader.Read() || reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject
                || reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
            { throw Invalid(); }
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != fields.Length)
        { throw Invalid(); }
    }

    private T Read<T>(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> fields, JsonTypeInfo<T> typeInfo) where T : struct
    {
        try
        {
            ValidateShape(bytes, fields);
            return JsonSerializer.Deserialize(bytes, typeInfo);
        }
        catch (JsonException)
        { throw Invalid(); }
        catch (DecoderFallbackException)
        { throw Invalid(); }
    }

    private bool ValidPrincipal(string value)
    {
        if (!value.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal))
        { return false; }
        try
        { return StrictUtf8.GetByteCount(value) <= executionOptions.Value.MaximumPrincipalBytes; }
        catch (EncoderFallbackException)
        { return false; }
    }

    private static InvalidOperationException Invalid() => new(InvalidRecord);
}
