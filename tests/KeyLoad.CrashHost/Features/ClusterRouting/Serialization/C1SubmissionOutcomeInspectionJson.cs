using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1SubmissionOutcomeInspectionJson
{
    private const int Base64InputGroup = 3;
    private const int Base64OutputGroup = 4;
    private const int Base64RoundingOffset = 2;
    private const int LineFeedBytes = 1;
    private const int JsonStartOffset = 0;
    private const byte LineFeed = (byte)'\n';
    private static readonly string[] RequestFields = [nameof(C1SubmissionOutcomeInspectionRequest.Version),
        nameof(C1SubmissionOutcomeInspectionRequest.Directory), nameof(C1SubmissionOutcomeInspectionRequest.ExpectedNodeId),
        nameof(C1SubmissionOutcomeInspectionRequest.Incarnation), nameof(C1SubmissionOutcomeInspectionRequest.PrincipalId),
        nameof(C1SubmissionOutcomeInspectionRequest.CommandId), nameof(C1SubmissionOutcomeInspectionRequest.Partition)];
    private static readonly string[] ReceiptFields = [nameof(C1SubmissionOutcomeInspectionReceipt.Version),
        nameof(C1SubmissionOutcomeInspectionReceipt.NodeId), nameof(C1SubmissionOutcomeInspectionReceipt.Incarnation),
        nameof(C1SubmissionOutcomeInspectionReceipt.FormatVersion), nameof(C1SubmissionOutcomeInspectionReceipt.Position),
        nameof(C1SubmissionOutcomeInspectionReceipt.OutcomeKey), nameof(C1SubmissionOutcomeInspectionReceipt.NativeStoredOutcome)];
    private static readonly C1SubmissionOutcomeInspectionJsonContext Context = new(new JsonSerializerOptions
    {
        MaxDepth = C1SubmissionOutcomeInspectionProtocol.MaximumJsonDepth,
        PropertyNameCaseInsensitive = false,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false
    });

    internal static C1SubmissionOutcomeInspectionRequest ReadRequest(ReadOnlySpan<byte> bytes)
    {
        C1OutcomeInspectionJson.ValidateShape(bytes, RequestFields,
            C1SubmissionOutcomeInspectionProtocol.MaximumRequestBytes, C1SubmissionOutcomeInspectionValidation.Invalid);
        try
        { return C1SubmissionOutcomeInspectionValidation.Request(JsonSerializer.Deserialize(bytes, Context.C1SubmissionOutcomeInspectionRequest)); }
        catch (JsonException) { throw C1SubmissionOutcomeInspectionValidation.Invalid(); }
    }

    internal static byte[] SerializeRequest(C1SubmissionOutcomeInspectionRequest request)
    {
        _ = C1SubmissionOutcomeInspectionValidation.Request(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Context.C1SubmissionOutcomeInspectionRequest);
        if (bytes.Length > C1SubmissionOutcomeInspectionProtocol.MaximumRequestBytes)
        { throw C1SubmissionOutcomeInspectionValidation.Invalid(); }
        return bytes;
    }

    internal static C1SubmissionOutcomeInspectionReceipt ReadReceipt(ReadOnlySpan<byte> bytes)
    {
        C1OutcomeInspectionJson.ValidateShape(bytes, ReceiptFields,
            C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes, C1SubmissionOutcomeInspectionValidation.Invalid);
        try
        { return C1SubmissionOutcomeInspectionValidation.Receipt(JsonSerializer.Deserialize(bytes, Context.C1SubmissionOutcomeInspectionReceipt)); }
        catch (JsonException) { throw C1SubmissionOutcomeInspectionValidation.Invalid(); }
    }

    internal static byte[] SerializeReceipt(C1SubmissionOutcomeInspectionReceipt receipt)
    {
        _ = C1SubmissionOutcomeInspectionValidation.Receipt(receipt);
        var ceiling = checked(EncodedBytes(receipt.OutcomeKey.Length) + EncodedBytes(receipt.NativeStoredOutcome.Length)
            + C1SubmissionOutcomeInspectionProtocol.MaximumMetadataBytes + LineFeedBytes);
        if (ceiling > C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes)
        { throw C1SubmissionOutcomeInspectionValidation.Invalid(); }
        var json = JsonSerializer.SerializeToUtf8Bytes(receipt, Context.C1SubmissionOutcomeInspectionReceipt);
        if (json.Length + LineFeedBytes > C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes)
        { throw C1SubmissionOutcomeInspectionValidation.Invalid(); }
        var line = new byte[json.Length + LineFeedBytes];
        json.CopyTo(line, JsonStartOffset);
        line[^LineFeedBytes] = LineFeed;
        return line;
    }

    private static int EncodedBytes(int length)
        => checked((length + Base64RoundingOffset) / Base64InputGroup * Base64OutputGroup);
}
