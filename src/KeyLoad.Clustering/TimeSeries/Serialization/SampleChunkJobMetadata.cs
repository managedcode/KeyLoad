using KeyLoad.Core.TimeSeries;
using Orleans.DurableJobs;

namespace KeyLoad.Clustering;

internal static class SampleChunkJobMetadata
{
    internal static ScheduleJobRequest Create(GrainId target, DateTimeOffset dueAt,
        SampleChunkWorkHint hint)
    {
        return new ScheduleJobRequest
        {
            Target = target,
            JobName = SampleChunkJobProtocol.JobName,
            DueTime = dueAt,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SampleChunkJobProtocol.HintKey] = Convert.ToBase64String(OrleansSerialization.Serialize(hint))
            },
            TraceParent = string.Empty,
            TraceState = string.Empty
        };
    }

    internal static SampleChunkWorkHint Read(IJobRunContext context, GrainId target)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = context.Job;
        if (job is null || job.TargetGrainId != target || job.Name != SampleChunkJobProtocol.JobName
            || job.Metadata is not { } metadata || metadata.Count != SampleChunkJobProtocol.MetadataCount
            || !metadata.TryGetValue(SampleChunkJobProtocol.HintKey, out var encoded)
            || string.IsNullOrEmpty(encoded))
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid);
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException) { throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid); }
        return OrleansSerialization.Deserialize<SampleChunkWorkHint>(bytes);
    }
}
