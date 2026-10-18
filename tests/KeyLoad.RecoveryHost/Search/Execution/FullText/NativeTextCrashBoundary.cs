using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryHost.Features.Search;

internal sealed class NativeTextCrashBoundary(NativeTextFaultStage expected)
{
    private const int GetObservedEmptyRead = 0;

    private int observed;
    internal bool Armed
    {
        get; set;
    }
    internal bool Observed => Volatile.Read(ref observed) != GetObservedEmptyRead;

    internal void Observe(NativeTextFaultStage stage)
    {
        const int ValueSingleItemCount = 1;
        const int EmptyExchange = 0;
        const string ObserveMessageText = "The requested native text stage was observed more than once.";

        if (!Armed || stage != expected)
        {
            return;
        }
        if (Interlocked.Exchange(ref observed, ValueSingleItemCount) != EmptyExchange)
        {
            throw new InvalidOperationException(ObserveMessageText);
        }
        RecoveryHostPause.AtBoundary();
    }
}
