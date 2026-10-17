using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Replication;

/// <summary>Reads borrowed forward frames and encodes only known, counted original outcomes.</summary>
internal static class ReplicaForwardReplyEncoding
{
    private const int NoNestedEntries = 0;
    private const int NoResultBytes = 0;
    private const string ResultExceeded = "The forwarded command result byte budget is exceeded.";

    internal static ReplicaForwardRequestV1 ReadRequest(ReadOnlyMemory<byte> payload, DatabaseEngine database)
    {
        var frame = ReplicaNativeInspection.Inspect<ReplicaForwardRequestV1>(payload, NoNestedEntries).Value;
        ReplicaForwardFrameValidation.RequireRequest(frame);
        if (frame.Grant is { } caps)
        {
            ReplicaForwardReadBudget.RequireCaps(caps, database.OperationLimitsOptions.Value);
        }
        return frame;
    }

    internal static void Constrain(ReplicaForwardRequestV1 frame, IReplicaForwardedCommandWork work)
    {
        work.Budget.RequireResourceLifetimeBinding(work.Binding);
        if (frame.Grant is { } caps)
        {
            work.Budget.ConstrainNativeWork(caps.MaximumReadBytes, caps.MaximumExaminedRecords);
            work.Budget.ConstrainResultBytes(caps.MaximumResultBytes);
            work.Budget.ConstrainLifetime(frame.ExpiresAt!.Value);
        }
        work.Budget.Check();
    }

    internal static bool CanAccount(ReplicaForwardRequestV1 frame, IReplicaForwardedCommandWork work,
        Exception? error)
        => frame.Grant is not null && !work.Budget.HasIndeterminateMetadata
            && error is KeyLoadException domain && domain.Code != ErrorCode.UnknownWriteOutcome;

    internal static byte[] Encode(ReplicaForwardRequestV1 request, ReadOnlyMemory<byte> result,
        KeyLoadException? error, IReplicaForwardedCommandWork? work)
    {
        var stats = request.Grant is null ? null : new ReplicaForwardReadStatsV1(
            work!.Budget.ReadBytes, work.Budget.ConstrainedNativeRecords, result.Length);
        var frame = new ReplicaForwardReplyV1(ReplicaForwardMetadataProtocol.CurrentVersion,
            result, error?.Code, error?.Message, stats);
        ReplicaForwardFrameValidation.RequireReply(frame, request.Grant is not null);
        if (request.Grant is { } caps)
        {
            ReplicaForwardReadBudget.RequireStats(caps, stats!, result.Length);
        }
        if (work is not null)
        {
            work.Budget.CheckResult(frame);
            if (ReplicaProtocolCodec.Measure(frame) > work.Budget.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResultExceeded);
            }
        }
        return ReplicaProtocolCodec.Serialize(frame);
    }

    internal static void Clear(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > NoResultBytes && MemoryMarshal.TryGetArray(payload, out var owned))
        {
            CryptographicOperations.ZeroMemory(owned.AsSpan());
        }
    }
}
