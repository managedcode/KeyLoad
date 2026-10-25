using KeyLoad.Core;
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
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        return AnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, available / CaptureCount);
    }

    private static long Scratch(ServerRuntimeOptions configured, ReadExecutionBudget budget)
        => checked(configured.Core.PackedAnn.Value.MaxScratchBytes
            + ((long)configured.Core.AnnSeed.Value.MaxRecords + BitmapRemainder) / BitmapWordBits * BitmapWordBytes
            + (long)ReplyBytes(configured, budget) * Utf16BytesPerWireByte
            + (long)configured.Core.QueryExecution.Value.MaximumSearchResults * RankingBytesPerResult);

    // One public ANN page returns as one grain reply, so its encoded result never exceeds the reply ceiling.
    private static int ReplyBytes(ServerRuntimeOptions configured, ReadExecutionBudget budget)
        => Math.Min(budget.MaximumResultBytes, configured.GrainRouting.Value.MaximumReplyBytes);
}
