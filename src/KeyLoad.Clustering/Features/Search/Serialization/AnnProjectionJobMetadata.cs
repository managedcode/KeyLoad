using System.Text;
using KeyLoad.Core;
using Orleans.DurableJobs;

namespace KeyLoad.Clustering;

internal static class AnnProjectionJobMetadata
{
    internal static ScheduleJobRequest Create(global::Orleans.Runtime.GrainId target, DateTimeOffset dueAt,
        AnnMaintenanceRequest original, RuntimeJournalOptions options)
    {
        var digest = AnnProjectionJobProtocol.RequestDigest(original);
        if (Encoding.UTF8.GetByteCount(AnnProjectionJobProtocol.RequestDigestKey)
            + Encoding.UTF8.GetByteCount(digest) > options.MaximumMetadataBytes
            || Encoding.UTF8.GetByteCount(digest) > options.MaximumNameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProjectionJobProtocol.Exhausted);
        }
        return new ScheduleJobRequest
        {
            Target = target,
            JobName = AnnProjectionJobProtocol.JobName,
            DueTime = dueAt,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AnnProjectionJobProtocol.RequestDigestKey] = digest
            },
            TraceParent = string.Empty,
            TraceState = string.Empty
        };
    }

    internal static void Require(IJobRunContext context, global::Orleans.Runtime.GrainId target,
        AnnProjectionJobState original)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = original.PendingRequest ?? original.LastRequest
            ?? throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
        var job = context.Job;
        if (job is null || job.TargetGrainId != target || job.Name != AnnProjectionJobProtocol.JobName
            || job.Metadata is not { } metadata || metadata.Count != AnnProjectionJobProtocol.MetadataCount
            || !metadata.TryGetValue(AnnProjectionJobProtocol.RequestDigestKey, out var digest)
            || digest != AnnProjectionJobProtocol.RequestDigest(request)
            || original.JobId is { } returned && returned != job.Id)
        {
            throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
        }
    }
}
