using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class OnlineTextFrameAccounting(IOptions<GrainRoutingOptions> options)
{
    private readonly int maximumFrames = options.Value.MaximumTotalFrames;
    // ExecuteCapabilityAsync awaited the actual parent StartedAsync before dispatching this parent body.
    private const int ParentStartedFrameCount = 1;
    private const int ProgressFrameIncrement = 1;
    private int observed = ParentStartedFrameCount;
    private const int ParentTerminalReservation = 1;

    internal void AdmitProgress()
    {
        if (observed >= maximumFrames - ParentTerminalReservation)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded); }
        observed = checked(observed + ProgressFrameIncrement);
    }

    internal async IAsyncEnumerable<T> ObserveChild<T>(IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in source.WithCancellation(cancellationToken).ConfigureAwait(true))
        {
            AdmitProgress();
            yield return chunk;
        }
    }
}
