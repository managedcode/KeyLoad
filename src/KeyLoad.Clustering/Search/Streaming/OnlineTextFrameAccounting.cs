using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class OnlineTextFrameAccounting(IOptions<GrainRoutingOptions> options)
{
    private readonly int maximumFrames = options.Value.MaximumTotalFrames;
    // The connected grain awaited its actual StartedAsync before dispatching this parent body.
    private const int ParentStartedFrameCount = 1;
    private const int ProgressFrameIncrement = 1;
    private int observed = ParentStartedFrameCount;
    private const int ParentTerminalReservation = 1;
    private const int TaskExchangeMessages = 2;

    internal void AdmitProgress() => Admit(ProgressFrameIncrement);

    // Keep the original whole-operation ceiling for the actual Task request/reply pair.
    internal void AdmitChildExchange() => Admit(TaskExchangeMessages);

    private void Admit(int count)
    {
        if (count > maximumFrames - ParentTerminalReservation - observed)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        observed = checked(observed + count);
    }
}
