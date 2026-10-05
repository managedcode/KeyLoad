using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] RequestFields =
    [
        nameof(C1OutcomeInspectionRequest.Version),
        nameof(C1OutcomeInspectionRequest.Directory),
        nameof(C1OutcomeInspectionRequest.ExpectedNodeId),
        nameof(C1OutcomeInspectionRequest.Incarnation),
        nameof(C1OutcomeInspectionRequest.PrincipalId),
        nameof(C1OutcomeInspectionRequest.CommandId)
    ];
    private static readonly string[] ReceiptFields =
    [
        nameof(C1OutcomeInspectionReceipt.Version),
        nameof(C1OutcomeInspectionReceipt.NodeId),
        nameof(C1OutcomeInspectionReceipt.Incarnation),
        nameof(C1OutcomeInspectionReceipt.FormatVersion),
        nameof(C1OutcomeInspectionReceipt.Position),
        nameof(C1OutcomeInspectionReceipt.OutcomePresent)
    ];
    private static readonly C1OutcomeInspectionJsonContext Context = CreateContext();

    internal static C1OutcomeInspectionRequest ReadRequest(ReadOnlySpan<byte> bytes)
    {
        ValidateShape(bytes, RequestFields, C1OutcomeInspectionProtocol.MaximumRequestBytes, InvalidRequest);
        try
        {
            var request = JsonSerializer.Deserialize(bytes, Context.C1OutcomeInspectionRequest);
            return C1OutcomeInspectionRequestValidation.Validate(request);
        }
        catch (JsonException)
        {
            throw InvalidRequest();
        }
        catch (DecoderFallbackException)
        {
            throw InvalidRequest();
        }
        catch (ArgumentException)
        {
            throw InvalidRequest();
        }
        catch (NotSupportedException)
        {
            throw InvalidRequest();
        }
    }

    internal static byte[] SerializeRequest(C1OutcomeInspectionRequest request)
    {
        _ = C1OutcomeInspectionRequestValidation.Validate(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Context.C1OutcomeInspectionRequest);
        if (bytes.Length > C1OutcomeInspectionProtocol.MaximumRequestBytes)
        {
            throw InvalidRequest();
        }
        return bytes;
    }

    internal static C1OutcomeInspectionReceipt ReadReceipt(ReadOnlySpan<byte> bytes)
    {
        ValidateShape(bytes, ReceiptFields, C1OutcomeInspectionProtocol.MaximumReceiptBytes, InvalidReceipt);
        try
        {
            var receipt = JsonSerializer.Deserialize(bytes, Context.C1OutcomeInspectionReceipt);
            if (receipt is null || receipt.Version != C1OutcomeInspectionProtocol.Version
                || receipt.NodeId == Guid.Empty || receipt.Incarnation == Guid.Empty
                || receipt.FormatVersion <= 0 || receipt.Position < 0)
            {
                throw InvalidReceipt();
            }
            return receipt;
        }
        catch (JsonException)
        {
            throw InvalidReceipt();
        }
    }

    internal static byte[] SerializeReceipt(C1OutcomeInspectionReceipt receipt)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(receipt, Context.C1OutcomeInspectionReceipt);
        var lineLength = checked(json.Length + 1);
        if (lineLength > C1OutcomeInspectionProtocol.MaximumReceiptBytes)
        {
            throw InvalidReceipt();
        }
        var line = new byte[lineLength];
        json.CopyTo(line);
        line[^1] = (byte)'\n';
        return line;
    }

    private static void ValidateShape(ReadOnlySpan<byte> bytes, string[] fields, int maximumBytes,
        Func<InvalidDataException> invalid)
    {
        try
        {
            ValidateShapeCore(bytes, fields, maximumBytes, invalid);
        }
        catch (JsonException)
        {
            throw invalid();
        }
    }

    private static void ValidateShapeCore(ReadOnlySpan<byte> bytes, string[] fields, int maximumBytes,
        Func<InvalidDataException> invalid)
    {
        if (bytes.IsEmpty || bytes.Length > maximumBytes)
        {
            throw invalid();
        }
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw invalid();
        }
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = C1OutcomeInspectionProtocol.MaximumJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw invalid();
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
            {
                throw invalid();
            }
            var name = reader.GetString();
            if (name is null || Array.IndexOf(fields, name) < 0 || !seen.Add(name)
                || !reader.Read() || reader.TokenType is JsonTokenType.Null
                    or JsonTokenType.StartArray or JsonTokenType.StartObject or JsonTokenType.EndArray or JsonTokenType.EndObject)
            {
                throw invalid();
            }
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != fields.Length)
        {
            throw invalid();
        }
    }

    private static C1OutcomeInspectionJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = C1OutcomeInspectionProtocol.MaximumJsonDepth,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            AllowDuplicateProperties = false
        };
        return new(options);
    }

    private static InvalidDataException InvalidRequest()
        => new(C1OutcomeInspectionProtocol.InvalidRequest);

    private static InvalidDataException InvalidReceipt()
        => new(C1OutcomeInspectionProtocol.InvalidReceipt);
}
