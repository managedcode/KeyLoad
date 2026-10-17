using System.Runtime.ExceptionServices;

namespace KeyLoad.Server;

internal sealed class OperationalAnalyticsNativeLogger : ZoneTree.Logger.ILogger
{
    private Exception? firstFailure;
    public ZoneTree.Logger.LogLevel LogLevel { get; set; } = ZoneTree.Logger.LogLevel.Error;
    public void LogError(Exception error) => Interlocked.CompareExchange(ref firstFailure, error, null);
    public void LogWarning(object value)
    {
    }
    public void LogInfo(object value)
    {
    }
    public void LogTrace(object value)
    {
    }

    internal void ThrowIfFailed()
    {
        if (Volatile.Read(ref firstFailure) is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
