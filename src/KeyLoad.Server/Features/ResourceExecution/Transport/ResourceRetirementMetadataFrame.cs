using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Bounds only the mutable private META frame; the saved original operation and DATA grant remain untouched.</summary>
internal static class ResourceRetirementMetadataFrame
{
    private const int ReplyCatalogRecords = 1;
    internal static ResourceRetirementNativeRequest Constrain(ResourceRetirementNativeRequest request,
        ReadExecutionBudgetReadGrant metadata, DatabaseLimits limits, out byte[]? rewritten)
    {
        rewritten = null;
        // Headroom is a protocol maximum, never reported or charged as an observation.
        var replyBytes = checked((long)PhysicalShardCatalogProtocol.MaximumEncodedBytes
            + PhysicalShardCatalogRecordSerialization.CatalogKey().LongLength);
        var bytes = Math.Min(request.MaximumReadBytes, metadata.RemainingBytes - replyBytes);
        var records = Math.Min(request.MaximumExaminedRecords, metadata.RemainingRecords - ReplyCatalogRecords);
        if (bytes <= ResourceRetirementReadContract.NoReadBytes || records <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }

        if (bytes == request.MaximumReadBytes && records == request.MaximumExaminedRecords)
        {
            return request;
        }

        rewritten = request.Command
            ? Encode(NativeSerialization.Deserialize<ResourceRetirementNativePhaseRequestV1>(request.Payload.Span)
                with
            {
                MaximumReadBytes = bytes,
                MaximumExaminedRecords = records
            }, limits)
            : Encode(NativeSerialization.Deserialize<ResourceRetirementReadRequestV1>(request.Payload.Span)
                with
            {
                MaximumReadBytes = bytes,
                MaximumExaminedRecords = records
            }, limits);
        return request with
        {
            MaximumReadBytes = bytes,
            MaximumExaminedRecords = records,
            Payload = rewritten,
            Signature = string.Empty
        };
    }

    internal static T Decode<T>(ResourceRetirementNativeRequest request, ResourceRetirementNativeReply reply,
        Action<ResourceRetirementNativeRequest, ResourceRetirementNativeReply>? capture) where T : class
    {
        try
        {
            var result = NativeSerialization.Deserialize<T>(reply.Payload.Span);
            if (result is ResourceRetirementReadReplyV1 read
                && (reply.Metadata is not { } observed || read.ReadBytes > observed.ReadBytes
                    || read.ExaminedRecords > observed.ExaminedRecords))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
            }

            capture?.Invoke(request, reply);
            return result;
        }
        catch (Exception error) when (request.Command && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ResourceRetirementNativeProtocol.Unavailable, error);
        }
    }

    private static byte[] Encode<T>(T value, DatabaseLimits limits)
    {
        if (NativeSerialization.Measure(value) > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }

        return NativeSerialization.Serialize(value);
    }
}
