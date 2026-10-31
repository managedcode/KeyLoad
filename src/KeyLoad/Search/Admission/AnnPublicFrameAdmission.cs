using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class AnnPublicFrameAdmission
{
    private const int CaptureCount = 2;
    private const int BitmapWordBits = 64;
    private const int BitmapRemainder = 63;
    private const int BitmapWordBytes = sizeof(ulong);
    private const int Empty = 0;
    private const int RankingBytesPerResult = 1_024;

    internal static long Desired(ServerRuntimeOptions configured)
    {
        var seed = configured.Core.AnnSeed.Value;
        seed.Validate();
        return checked(CaptureCount * seed.MaxPeakBytes + Scratch(configured));
    }

    internal static IOptions<AnnSeedOptions> CaptureOptions(ServerRuntimeOptions configured, long reserved)
    {
        var available = checked(reserved - Scratch(configured));
        if (available <= Empty)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        return AnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, available / CaptureCount);
    }

    private static long Scratch(ServerRuntimeOptions configured)
        => checked(configured.Core.PackedAnn.Value.MaxScratchBytes
            + ((long)configured.Core.AnnSeed.Value.MaxRecords + BitmapRemainder) / BitmapWordBits * BitmapWordBytes
            + (long)configured.Core.QueryExecution.Value.MaximumSearchResults * RankingBytesPerResult);
}
