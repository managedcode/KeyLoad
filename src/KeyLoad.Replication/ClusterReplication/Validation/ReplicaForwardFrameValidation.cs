using System.Text;

namespace KeyLoad.Replication;

/// <summary>Checks the closed transient frame without reading database state or issuing operation authority.</summary>
internal static class ReplicaForwardFrameValidation
{
    private const int NoNestedEntries = 0;
    private const int NoNativeWork = 0;

    internal static void RequireRequest(ReplicaForwardRequestV1 frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Require(frame.Version == ReplicaForwardMetadataProtocol.CurrentVersion && !frame.Operation.IsEmpty);
        _ = ReplicaProtocolCodec.PayloadBytes(frame.Operation.Span);
        Require((frame.Grant is null) == (frame.ExpiresAt is null));
        var operation = ReplicaNativeInspection.Inspect<ReplicatedOperation>(frame.Operation, maximumEntries: NoNestedEntries).Value;
        Require(operation.Kind != OperationKind.ResourceRetirementPhase || frame.Grant is not null);
        if (frame.Grant is { } grant)
        {
            Require(grant.MaximumReadBytes > NoNativeWork && grant.MaximumExaminedRecords > NoNativeWork && grant.MaximumResultBytes > NoNativeWork
                && frame.ExpiresAt > DateTimeOffset.MinValue && frame.ExpiresAt < DateTimeOffset.MaxValue);
        }
    }

    internal static void RequireReply(ReplicaForwardReplyV1 frame, bool budgeted)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Require(frame.Version == ReplicaForwardMetadataProtocol.CurrentVersion
            && (frame.Statistics is not null) == budgeted);
        if (frame.Error is { } error)
        {
            Require(Enum.IsDefined(error) && frame.Result.IsEmpty && !string.IsNullOrWhiteSpace(frame.SafeDetail));
            Require(frame.SafeDetail!.Length <= ReplicaProtocol.MaximumDetailCharacters
                && Encoding.UTF8.GetByteCount(frame.SafeDetail) <= ReplicaProtocol.PayloadMetadataBytes);
        }
        else
        {
            Require(frame.SafeDetail is null && !frame.Result.IsEmpty);
            _ = ReplicaProtocolCodec.PayloadBytes(frame.Result.Span);
        }
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
    }
}
