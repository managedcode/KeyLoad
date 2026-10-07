namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleRollupReadCharge(long maximum)
{
    private long used;
    internal void Charge(long bytes)
    {
        if (bytes > maximum - used)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SampleRollupProtocol.ByteBudget); }
        used += bytes;
    }
}
