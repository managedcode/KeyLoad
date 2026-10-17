using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportCaptureRequest
{
    private const int Frames = 2;
    private const int RecordOverhead = 512;
    private const int SessionOverhead = 4096;

    internal static byte[] Body(D2ImportSession session, PartitionMovePhaseResult accepted)
    {
        var limits = session.Source.Database.Limits;
        var memory = session.SourceRuntime.Core.CacheMemory.Value;
        var records = Math.Min(limits.MaxScanRecords, memory.MaxRetainedEntries);
        var overhead = checked((long)records * RecordOverhead
            + (long)PartitionRecordFamilies.All.Length * RecordOverhead
            + Frames * limits.MaxBatchBytes + SessionOverhead);
        var available = checked(memory.MaxRetainedBytes - overhead);
        var imageBytes = Math.Min(Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes), available / Frames);
        var pageBytes = checked((int)(imageBytes / Frames));
        if (imageBytes <= D2ImportCrashProtocol.Empty || pageBytes <= D2ImportCrashProtocol.Empty)
        {
            throw new InvalidOperationException("The original native capture budget cannot retain this image.");
        }
        return NativeSerialization.Serialize(new PartitionMoveCaptureRequest(D2ImportCrashProtocol.Principal,
            accepted.Fence!, pageBytes, imageBytes, records));
    }
}
