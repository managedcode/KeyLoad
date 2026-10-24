using System.Runtime.ExceptionServices;

namespace KeyLoad.Core.AnalyticsStore;

/// <summary>Retains the first execution storage failure without publishing events or dimensions to telemetry.</summary>
internal sealed class AnalyticsStoreFailures : ZoneTree.Logger.ILogger
{
    private Exception? failure;
    public ZoneTree.Logger.LogLevel LogLevel { get; set; } = ZoneTree.Logger.LogLevel.Error;
    public void LogError(Exception error) => Interlocked.CompareExchange(ref failure, error, null);
    public void LogWarning(object value)
    {
    }
    public void LogInfo(object value)
    {
    }
    public void LogTrace(object value)
    {
    }

    internal void RequireHealthy()
    {
        if (Volatile.Read(ref failure) is { } original)
        {
            ExceptionDispatchInfo.Capture(original).Throw();
        }
    }
}
