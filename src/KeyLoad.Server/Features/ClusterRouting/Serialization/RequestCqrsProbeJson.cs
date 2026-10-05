using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeJson
{
    private const int MaximumRecordBytes = 8_192;
    private const string InvalidRecord = "The private request probe record is invalid.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly RequestCqrsProbeJsonContext Context = CreateContext();

    internal static RequestCqrsProbeOwnerRecord ReadOwner(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Owner, Context.RequestCqrsProbeOwnerRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.OwnerKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || string.IsNullOrWhiteSpace(value.Voter))
        { throw Invalid(); }
        return value;
    }

    internal static RequestCqrsProbeArmRecord ReadArm(ReadOnlySpan<byte> bytes)
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

    internal static RequestCqrsProbeReleaseRecord ReadRelease(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Release, Context.RequestCqrsProbeReleaseRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.ReleaseKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty)
        { throw Invalid(); }
        return value;
    }

    internal static RequestCqrsProbeMarkerRecord ReadMarker(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Marker, Context.RequestCqrsProbeMarkerRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.MarkerKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty
            || !Enum.IsDefined(value.Phase) || !Enum.IsDefined(value.Outcome)
            || string.IsNullOrWhiteSpace(value.Voter) || string.IsNullOrWhiteSpace(value.SiloAddress))
        { throw Invalid(); }
        return value;
    }

    internal static byte[] WriteMarker(RequestCqrsProbeMarkerRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Context.RequestCqrsProbeMarkerRecord);
        if (bytes.Length > MaximumRecordBytes)
        { throw Invalid(); }
        return bytes;
    }

    private static RequestCqrsProbeJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = RequestCqrsProbeProtocol.MaximumJsonDepth,
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

    private static void ValidateShape(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> fields)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumRecordBytes)
        { throw Invalid(); }
        try
        { _ = StrictUtf8.GetCharCount(bytes); }
        catch (DecoderFallbackException)
        { throw Invalid(); }
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = RequestCqrsProbeProtocol.MaximumJsonDepth,
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
            if (name is null || fields.IndexOf(name) < 0 || !seen.Add(name)
                || !reader.Read() || reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject
                || reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
            { throw Invalid(); }
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != fields.Length)
        { throw Invalid(); }
    }

    private static T Read<T>(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> fields, JsonTypeInfo<T> typeInfo) where T : struct
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

    private static bool ValidPrincipal(string value)
    {
        if (!value.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal))
        { return false; }
        try
        { return StrictUtf8.GetByteCount(value) <= RequestCqrsProbeProtocol.MaximumPrincipalBytes; }
        catch (EncoderFallbackException)
        { return false; }
    }

    private static InvalidOperationException Invalid() => new(InvalidRecord);
}
