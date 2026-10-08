using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPublicFrameAdmission
{
    private const int CaptureCount = 2;
    private const int BitmapWordBits = 64;
    private const int BitmapRemainder = 63;
    private const int BitmapWordBytes = sizeof(ulong);
    private const int Empty = 0;
    private const int Utf16BytesPerWireByte = 2;
    private const int RankingBytesPerResult = 1_024;

    internal static long Desired(ServerRuntimeOptions configured, ReadExecutionBudget budget)
    {
        var seed = configured.Core.AnnSeed.Value;
        seed.Validate();
        return checked(CaptureCount * seed.MaxPeakBytes + Scratch(configured, budget));
    }

    internal static IOptions<AnnSeedOptions> CaptureOptions(ServerRuntimeOptions configured, ReadExecutionBudget budget, long reserved)
    {
        var available = checked(reserved - Scratch(configured, budget));
        if (available <= Empty)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        return NativeAnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, available / CaptureCount);
    }

    private static long Scratch(ServerRuntimeOptions configured, ReadExecutionBudget budget)
        => checked(configured.Core.PackedAnn.Value.MaxScratchBytes
            + ((long)configured.Core.AnnSeed.Value.MaxRecords + BitmapRemainder) / BitmapWordBits * BitmapWordBytes
            + (long)budget.MaximumResultBytes * Utf16BytesPerWireByte
            + (long)configured.Core.QueryExecution.Value.MaximumSearchResults * RankingBytesPerResult);
}
