using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class C1SubmissionJournalInspectionJson
{
    private const int NoItems = 0;
    private const int Base64InputGroup = 3;
    private const int Base64OutputGroup = 4;
    private const int Base64RoundingOffset = 2;
    private const int LineFeedBytes = 1;
    private const byte LineFeed = (byte)'\n';
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] RequestFields = [nameof(C1SubmissionJournalInspectionRequest.Version),
        nameof(C1SubmissionJournalInspectionRequest.NodeDirectory), nameof(C1SubmissionJournalInspectionRequest.ExpectedCanonicalNodeId),
        nameof(C1SubmissionJournalInspectionRequest.Incarnation), nameof(C1SubmissionJournalInspectionRequest.LocalVoter),
        nameof(C1SubmissionJournalInspectionRequest.Voters), nameof(C1SubmissionJournalInspectionRequest.EntryIndex),
        nameof(C1SubmissionJournalInspectionRequest.EntryTerm)];
    private static readonly string[] ReceiptFields = [nameof(C1SubmissionJournalInspectionReceipt.Version),
        nameof(C1SubmissionJournalInspectionReceipt.CanonicalNodeId), nameof(C1SubmissionJournalInspectionReceipt.ReplicaNodeId),
        nameof(C1SubmissionJournalInspectionReceipt.Incarnation), nameof(C1SubmissionJournalInspectionReceipt.CanonicalPosition),
        nameof(C1SubmissionJournalInspectionReceipt.ReplicaPosition), nameof(C1SubmissionJournalInspectionReceipt.NativeHardState),
        nameof(C1SubmissionJournalInspectionReceipt.NativeEntry)];
    private static readonly C1SubmissionJournalInspectionJsonContext Context = new(new JsonSerializerOptions
    {
        MaxDepth = C1SubmissionJournalInspectionProtocol.MaximumJsonDepth,
        PropertyNameCaseInsensitive = false, IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false
    });
    internal static C1SubmissionJournalInspectionRequest ReadRequest(ReadOnlySpan<byte> bytes)
    {
        ValidateRequestShape(bytes);
        try
        { return C1SubmissionJournalInspectionValidation.Request(JsonSerializer.Deserialize(bytes, Context.C1SubmissionJournalInspectionRequest)); }
        catch (JsonException) { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
    internal static byte[] SerializeRequest(C1SubmissionJournalInspectionRequest request)
    {
        _ = C1SubmissionJournalInspectionValidation.Request(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Context.C1SubmissionJournalInspectionRequest);
        if (bytes.Length > C1SubmissionJournalInspectionProtocol.MaximumRequestBytes)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        return bytes;
    }
    internal static C1SubmissionJournalInspectionReceipt ReadReceipt(ReadOnlySpan<byte> bytes)
    {
        C1OutcomeInspectionJson.ValidateShape(bytes, ReceiptFields,
            C1SubmissionJournalInspectionProtocol.MaximumReceiptBytes, C1SubmissionJournalInspectionValidation.Invalid);
        try
        { return C1SubmissionJournalInspectionValidation.Receipt(JsonSerializer.Deserialize(bytes, Context.C1SubmissionJournalInspectionReceipt)); }
        catch (JsonException) { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
    internal static byte[] SerializeReceipt(C1SubmissionJournalInspectionReceipt receipt)
    {
        _ = C1SubmissionJournalInspectionValidation.Receipt(receipt);
        var ceiling = checked(EncodedBytes(receipt.NativeHardState.Length) + EncodedBytes(receipt.NativeEntry.Length)
            + C1SubmissionJournalInspectionProtocol.MaximumMetadataBytes + LineFeedBytes);
        if (ceiling > C1SubmissionJournalInspectionProtocol.MaximumReceiptBytes)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        var json = JsonSerializer.SerializeToUtf8Bytes(receipt, Context.C1SubmissionJournalInspectionReceipt);
        if (json.Length + LineFeedBytes > C1SubmissionJournalInspectionProtocol.MaximumReceiptBytes)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        var line = new byte[json.Length + LineFeedBytes];
        json.CopyTo(line, NoItems); line[^LineFeedBytes] = LineFeed;
        return line;
    }
    private static int EncodedBytes(int length)
        => checked((length + Base64RoundingOffset) / Base64InputGroup * Base64OutputGroup);
    private static void ValidateRequestShape(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > C1SubmissionJournalInspectionProtocol.MaximumRequestBytes)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            ValidateRequestFields(bytes);
        }
        catch (JsonException) { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        catch (DecoderFallbackException) { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
    private static void ValidateRequestFields(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        { MaxDepth = C1SubmissionJournalInspectionProtocol.MaximumJsonDepth });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
            { throw C1SubmissionJournalInspectionValidation.Invalid(); }
            var name = reader.GetString();
            if (name is null || Array.IndexOf(RequestFields, name) < NoItems || !seen.Add(name) || !reader.Read())
            { throw C1SubmissionJournalInspectionValidation.Invalid(); }
            ValidateValue(ref reader, name);
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != RequestFields.Length)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
    private static void ValidateValue(ref Utf8JsonReader reader, string name)
    {
        if (name != nameof(C1SubmissionJournalInspectionRequest.Voters))
        {
            if (reader.TokenType is not (JsonTokenType.Number or JsonTokenType.String))
            { throw C1SubmissionJournalInspectionValidation.Invalid(); }
            return;
        }
        if (reader.TokenType != JsonTokenType.StartArray) { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        var count = NoItems;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String || ++count > C1SubmissionJournalInspectionProtocol.RequiredVoters)
            { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        }
        if (reader.TokenType != JsonTokenType.EndArray || count != C1SubmissionJournalInspectionProtocol.RequiredVoters)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
}
