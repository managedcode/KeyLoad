using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class NativeCommandObservationEncoding
{
    internal static GrainOperationReply Encode(NativeCommandObservationResultV1 result, ReadExecutionBudget work,
        IOptions<GrainRoutingOptions> options, CancellationToken token)
    {
        work.Check();
        var maximum = Math.Min(work.MaximumResultBytes, options.Value.MaximumReplyBytes);
        RequireSize(result, work, maximum);
        using var stream = new GrainBoundedPayloadStream(maximum, options, token);
        NativeSerialization.Serialize(result, stream);
        work.Check();
        return new()
        {
            Payload = stream.Complete()
        };
    }

    private static void RequireSize(NativeCommandObservationResultV1 result, ReadExecutionBudget work, int maximum)
    {
        // The allowance already excludes retained D1 bytes; counting must not tighten that raw limit again.
        using var json = new ResultByteCounterStream(maximum, work.Check);
        System.Text.Json.JsonSerializer.Serialize(json, result.Result.NativeValue, JsonDefaults.Options);
        using var native = new ResultByteCounterStream(maximum, work.Check);
        NativeSerialization.Serialize(result, native);
        work.Check();
    }

    internal static void Clear(GrainOperationReply? reply)
    {
        if (reply is null || reply.Payload.IsEmpty)
        {
            return;
        }
        if (!MemoryMarshal.TryGetArray(reply.Payload, out var owned))
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        CryptographicOperations.ZeroMemory(owned.AsSpan());
    }
}
