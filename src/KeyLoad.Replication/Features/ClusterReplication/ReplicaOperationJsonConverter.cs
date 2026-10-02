using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Replication;

internal sealed class ReplicaOperationJsonConverter : JsonConverter<ReplicatedOperation>
{
    internal const string IdField = "id";
    internal const string KindField = "kind";
    internal const string PrincipalField = "principalId";
    internal const string EvaluatedField = "evaluatedAt";
    internal const string PayloadField = "payloadJson";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <inheritdoc />
    public override ReplicatedOperation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(ReplicaPersistence.InvalidEncoding);
        }
        var fields = new ReplicaOperationFields();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException(ReplicaPersistence.InvalidEncoding);
            }
            var name = reader.GetString();
            if (!reader.Read())
            { throw new JsonException(ReplicaPersistence.InvalidEncoding); }
            fields.Read(name, ref reader, options);
        }
        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException(ReplicaPersistence.InvalidEncoding);
        }
        return fields.Build();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ReplicatedOperation value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString(IdField, value.Id);
        writer.WritePropertyName(KindField);
        JsonSerializer.Serialize(writer, value.Kind, options);
        writer.WriteString(PrincipalField, value.PrincipalId);
        writer.WriteString(EvaluatedField, value.EvaluatedAt);
        writer.WriteBase64String(PayloadField, Utf8.GetBytes(value.PayloadJson));
        writer.WriteEndObject();
    }

    internal static string DecodePayload(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetBytesFromBase64(out var bytes))
        {
            throw new JsonException(ReplicaPersistence.InvalidEncoding);
        }
        try
        { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException)
        {
            throw new JsonException(ReplicaPersistence.InvalidEncoding);
        }
    }
}

internal sealed class ReplicaOperationFields
{
    [Flags]
    private enum Fields { None = 0, Id = 1, Kind = 2, Principal = 4, Evaluated = 8, Payload = 16, All = Id | Kind | Principal | Evaluated | Payload }
    private Fields present;
    private Guid id;
    private OperationKind kind;
    private string? principal;
    private DateTimeOffset evaluated;
    private string? payload;

    internal void Read(string? name, ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var field = name switch
        {
            ReplicaOperationJsonConverter.IdField => Fields.Id,
            ReplicaOperationJsonConverter.KindField => Fields.Kind,
            ReplicaOperationJsonConverter.PrincipalField => Fields.Principal,
            ReplicaOperationJsonConverter.EvaluatedField => Fields.Evaluated,
            ReplicaOperationJsonConverter.PayloadField => Fields.Payload,
            _ => throw new JsonException(ReplicaPersistence.InvalidEncoding)
        };
        if ((present & field) != 0)
        { throw new JsonException(ReplicaPersistence.InvalidEncoding); }
        present |= field;
        switch (field)
        {
            case Fields.Id:
                if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out id))
                { throw new JsonException(ReplicaPersistence.InvalidEncoding); }
                break;
            case Fields.Kind:
                kind = JsonSerializer.Deserialize<OperationKind>(ref reader, options);
                if (!Enum.IsDefined(kind))
                { throw new JsonException(ReplicaPersistence.InvalidEncoding); }
                break;
            case Fields.Principal:
                if (reader.TokenType != JsonTokenType.String)
                { throw new JsonException(ReplicaPersistence.InvalidEncoding); }
                principal = reader.GetString();
                break;
            case Fields.Evaluated:
                if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out evaluated))
                { throw new JsonException(ReplicaPersistence.InvalidEncoding); }
                break;
            case Fields.Payload:
                payload = ReplicaOperationJsonConverter.DecodePayload(ref reader);
                break;
            default:
                throw new JsonException(ReplicaPersistence.InvalidEncoding);
        }
    }

    internal ReplicatedOperation Build()
    {
        if (present != Fields.All || principal is null || payload is null)
        {
            throw new JsonException(ReplicaPersistence.InvalidEncoding);
        }
        return new(id, kind, principal, evaluated, payload);
    }
}
