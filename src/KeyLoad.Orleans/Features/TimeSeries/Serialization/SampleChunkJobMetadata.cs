using KeyLoad.Core.Features.TimeSeries;
using Orleans.DurableJobs;

namespace KeyLoad.Orleans;

internal static class SampleChunkJobMetadata
{
    private const int MaximumBase64Expansion = 2;
    internal static ScheduleJobRequest Create(GrainId target, DateTimeOffset dueAt,
        SampleChunkWorkHint hint, int maximumBytes)
    {
        if (NativeSerialization.Measure(hint) > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkJobProtocol.Exhausted); }
        return new ScheduleJobRequest
        {
            Target = target, JobName = SampleChunkJobProtocol.JobName, DueTime = dueAt,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            { [SampleChunkJobProtocol.HintKey] = Convert.ToBase64String(NativeSerialization.Serialize(hint)) },
            TraceParent = string.Empty, TraceState = string.Empty
        };
    }

    internal static SampleChunkWorkHint Read(IJobRunContext context, GrainId target, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = context.Job;
        if (job is null || job.TargetGrainId != target || job.Name != SampleChunkJobProtocol.JobName
            || job.Metadata is not { } metadata || metadata.Count != SampleChunkJobProtocol.MetadataCount
            || !metadata.TryGetValue(SampleChunkJobProtocol.HintKey, out var encoded)
            || string.IsNullOrEmpty(encoded) || encoded.Length > checked(maximumBytes * MaximumBase64Expansion))
        { throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid); }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid); }
        if (bytes.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkJobProtocol.Exhausted); }
        return NativeSerialization.Deserialize<SampleChunkWorkHint>(bytes);
    }
}
