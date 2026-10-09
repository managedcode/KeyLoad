namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleChunkReadCharge(long maximum)
{
    private long used;
    internal void Charge(long bytes)
    {
        if (bytes < SampleChunkLifecycleProtocol.Absent || bytes > maximum - used)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkLifecycleProtocol.Exhausted); }
        used = checked(used + bytes);
    }
}
