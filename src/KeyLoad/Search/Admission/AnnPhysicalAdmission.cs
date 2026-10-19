using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class AnnPhysicalAdmission
{
    private const int FileTransientChunks = 3;
    internal static long Construction(PackedAnnAdmission plan, long seedPeak)
        => checked(seedPeak + plan.RetainedBytes + plan.BuildScratchBytes
            + FileTransientChunks * (long)PackedAnnStorageFrames.MaximumChunkBytes);
}
