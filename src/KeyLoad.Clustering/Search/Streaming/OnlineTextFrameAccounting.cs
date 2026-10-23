using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class OnlineTextFrameAccounting(IOptions<GrainRoutingOptions> options)
{
    private readonly int maximumFrames = options.Value.MaximumTotalFrames;
    // The connected grain awaited its actual StartedAsync before dispatching this parent body.
    private int observed = 1;
    private const int TaskExchangeMessages = 2;

    internal void AdmitProgress() => Admit(1);

    // Keep the original whole-operation ceiling for the actual Task request/reply pair.
    internal void AdmitChildExchange() => Admit(TaskExchangeMessages);

    private void Admit(int count)
    {
        if (count > maximumFrames - 1 - observed)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        observed = checked(observed + count);
    }
}
