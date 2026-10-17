using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost;

internal static class ExistingStoreInspectorProtocol
{
    internal const string Mode = "existing-store-inspect";
    internal const string ReadyMarker = "existing-store-inspector-ready";
    internal const int MaximumRequestCharacters = 4096;
    internal const int MaximumReceiptCharacters = 8192;
    internal const int MaximumValueBytes = 256;
    internal const int MaximumFailureTypes = 64;
    internal const int SchemaVersion = 1;
    internal const int InvalidProtocolExitCode = 2;
    private const string InvalidRequest = "The original store inspection request is invalid.";
    private const string InvalidReceipt = "The original store inspection receipt exceeds its bounded protocol.";

    internal static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    internal static ExistingStoreInspectionRequest DeserializeRequest(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaximumRequestCharacters)
        {
            throw new InvalidDataException(InvalidRequest);
        }
        var request = JsonSerializer.Deserialize<ExistingStoreInspectionRequest>(json, JsonOptions);
        if (request is null || !Enum.IsDefined(request.Variant))
        {
            throw new InvalidDataException(InvalidRequest);
        }
        return request;
    }

    internal static string SerializeReceipt(ExistingStoreInspectionReceipt receipt)
    {
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        if (json.Length + Environment.NewLine.Length > MaximumReceiptCharacters)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        return json;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false
        };
        options.Converters.Add(new JsonStringEnumConverter<ExistingStoreInspectionVariant>(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
