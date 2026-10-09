using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class NativeInstallFrameInspectionJson
{
    private const int MinimumPositiveCount = 1;
    private const int ChecksumTextLength = 64;
    private static readonly string[] RequestFields =
        [nameof(NativeInstallFrameInspectionRequest.Version), nameof(NativeInstallFrameInspectionRequest.Directory),
        nameof(NativeInstallFrameInspectionRequest.ExpectedNodeId), nameof(NativeInstallFrameInspectionRequest.Incarnation),
        nameof(NativeInstallFrameInspectionRequest.PrincipalId), nameof(NativeInstallFrameInspectionRequest.CommandId),
        nameof(NativeInstallFrameInspectionRequest.Partition), nameof(NativeInstallFrameInspectionRequest.MaximumFrameBytes)];
    private static readonly string[] ReceiptFields =
        [nameof(NativeInstallFrameInspectionReceipt.Version), nameof(NativeInstallFrameInspectionReceipt.NodeId),
        nameof(NativeInstallFrameInspectionReceipt.Incarnation), nameof(NativeInstallFrameInspectionReceipt.CommandId),
        nameof(NativeInstallFrameInspectionReceipt.FormatVersion), nameof(NativeInstallFrameInspectionReceipt.FrameSequence),
        nameof(NativeInstallFrameInspectionReceipt.PayloadBytes), nameof(NativeInstallFrameInspectionReceipt.PayloadSha256),
        nameof(NativeInstallFrameInspectionReceipt.MutationCount), nameof(NativeInstallFrameInspectionReceipt.RawMutationBytes),
        nameof(NativeInstallFrameInspectionReceipt.ExaminedFrames), nameof(NativeInstallFrameInspectionReceipt.ExaminedBytes), nameof(NativeInstallFrameInspectionReceipt.MaximumObservedPayloadBytes),
        nameof(NativeInstallFrameInspectionReceipt.OutcomeError), nameof(NativeInstallFrameInspectionReceipt.OutcomeStage),
        nameof(NativeInstallFrameInspectionReceipt.Installed), nameof(NativeInstallFrameInspectionReceipt.EncodedFrameLimitRejected)];

    internal static NativeInstallFrameInspectionRequest ReadRequest(ReadOnlySpan<byte> bytes)
    {
        C1OutcomeInspectionJson.ValidateShape(bytes, RequestFields, C1OutcomeInspectionProtocol.MaximumRequestBytes, InvalidRequest);
        try
        {
            return Validate(JsonSerializer.Deserialize(bytes, C1OutcomeInspectionJson.SharedContext.NativeInstallFrameInspectionRequest));
        }
        catch (JsonException) { throw InvalidRequest(); }
    }

    internal static byte[] SerializeRequest(NativeInstallFrameInspectionRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Validate(request), C1OutcomeInspectionJson.SharedContext.NativeInstallFrameInspectionRequest);
        if (bytes.Length > C1OutcomeInspectionProtocol.MaximumRequestBytes) { throw InvalidRequest(); }
        return bytes;
    }

    internal static byte[] SerializeReceipt(NativeInstallFrameInspectionReceipt receipt)
    {
        ValidateReceipt(receipt);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, C1OutcomeInspectionJson.SharedContext.NativeInstallFrameInspectionReceipt);
        if (bytes.Length > C1OutcomeInspectionProtocol.MaximumReceiptBytes) { throw InvalidReceipt(); }
        return bytes;
    }

    internal static NativeInstallFrameInspectionReceipt ReadReceipt(ReadOnlySpan<byte> bytes)
    {
        C1OutcomeInspectionJson.ValidateShape(bytes, ReceiptFields, C1OutcomeInspectionProtocol.MaximumReceiptBytes, InvalidReceipt);
        try
        {
            return ValidateReceipt(JsonSerializer.Deserialize(bytes, C1OutcomeInspectionJson.SharedContext.NativeInstallFrameInspectionReceipt));
        }
        catch (JsonException) { throw InvalidReceipt(); }
    }

    private static NativeInstallFrameInspectionRequest Validate(NativeInstallFrameInspectionRequest? request)
    {
        if (request is null) { throw InvalidRequest(); }
        _ = C1OutcomeInspectionRequestValidation.Validate(new(request.Version, request.Directory, request.ExpectedNodeId,
            request.Incarnation, request.PrincipalId, request.CommandId, request.Partition));
        new ZoneTreeStorageExecutionOptions { MaxFrameBytes = request.MaximumFrameBytes }.Validate();
        return request;
    }

    private static NativeInstallFrameInspectionReceipt ValidateReceipt(NativeInstallFrameInspectionReceipt? receipt)
    {
        if (receipt is null || receipt.Version != C1OutcomeInspectionProtocol.Version || receipt.NodeId == Guid.Empty
            || receipt.Incarnation == Guid.Empty || receipt.CommandId == Guid.Empty || receipt.FormatVersion < MinimumPositiveCount
            || receipt.FrameSequence < MinimumPositiveCount || receipt.PayloadBytes < MinimumPositiveCount
            || receipt.MutationCount < MinimumPositiveCount || receipt.RawMutationBytes < MinimumPositiveCount
            || receipt.ExaminedFrames < MinimumPositiveCount || receipt.ExaminedBytes < receipt.PayloadBytes
            || receipt.MaximumObservedPayloadBytes < receipt.PayloadBytes
            || receipt.PayloadSha256 is not { Length: ChecksumTextLength }
            || receipt.PayloadSha256.Any(character => !char.IsAsciiHexDigit(character))
            || receipt.OutcomeError != NativeInstallFrameInspectionProtocol.NoErrorOrStage && !Enum.IsDefined((ErrorCode)receipt.OutcomeError)
            || receipt.OutcomeStage != NativeInstallFrameInspectionProtocol.NoErrorOrStage && !Enum.IsDefined((PartitionMovePeerStage)receipt.OutcomeStage)
            || receipt.Installed && (receipt.OutcomeError != NativeInstallFrameInspectionProtocol.NoErrorOrStage
                || receipt.OutcomeStage != (int)PartitionMovePeerStage.Install)
            || receipt.EncodedFrameLimitRejected && receipt.OutcomeError != (int)ErrorCode.ResourceExhausted)
        { throw InvalidReceipt(); }
        return receipt;
    }

    private static InvalidDataException InvalidRequest() => new(C1OutcomeInspectionProtocol.InvalidRequest);
    private static InvalidDataException InvalidReceipt() => new(NativeInstallFrameInspectionProtocol.InvalidReceipt);
}
