using KeyLoad.Server.Search;

namespace KeyLoad.RecoveryTests.Search;

internal sealed class TextCrashBoundary(TextFaultStage expected)
{
    private int observed;
    internal bool Armed
    {
        get; set;
    }
    internal bool Observed => Volatile.Read(ref observed) != 0;

    internal void Observe(TextFaultStage stage)
    {
        const int ValueSingleItemCount = 1;
        const int EmptyExchange = 0;
        const string ObserveMessageText = "The requested execution text stage was observed more than once.";

        if (!Armed || stage != expected)
        {
            return;
        }
        if (Interlocked.Exchange(ref observed, ValueSingleItemCount) != EmptyExchange)
        {
            throw new InvalidOperationException(ObserveMessageText);
        }
        RecoveryProcessPause.AtBoundary();
    }
}
