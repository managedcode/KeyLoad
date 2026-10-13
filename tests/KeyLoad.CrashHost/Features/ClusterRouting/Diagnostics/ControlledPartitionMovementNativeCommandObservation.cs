using System.Globalization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeCommandObservation(
    OperationKind kind, Guid commandId, Guid requestId, TimeProvider clock)
{
    private const int FirstFailure = 0;
    private const string Format = "NativeMovementCommandFailure request={0:N} command={1:N} kind={2} phase={3} callerCancelled={4} elapsedTicks={5} category={6} error={7} failureCount={8}";
    private static readonly System.Text.CompositeFormat ReportFormat = System.Text.CompositeFormat.Parse(Format);
    private readonly long started = clock.GetTimestamp();
    internal Stage Phase { get; set; } = Stage.ContextAdmission;

    internal void Report(IReadOnlyList<Exception> failures, CancellationToken caller)
    {
        if (failures.Count == FirstFailure)
        { return; }
        var original = failures[FirstFailure];
        var category = original switch
        {
            KeyLoadException => Cause.Domain,
            OperationCanceledException => Cause.Cancellation,
            TimeoutException => Cause.Timeout,
            AggregateException => Cause.Aggregate,
            ObjectDisposedException => Cause.Disposed,
            _ => Cause.Other
        };
        var code = original is KeyLoadException failure && Enum.IsDefined(failure.Code)
            ? failure.Code : (ErrorCode?)null;
        Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, ReportFormat,
            requestId, commandId, kind, Phase, caller.IsCancellationRequested,
            clock.GetElapsedTime(started).Ticks, category, code, failures.Count));
    }

    internal enum Stage { ContextAdmission, Execution, ContextSettlement }
    private enum Cause { Domain, Cancellation, Timeout, Aggregate, Disposed, Other }
}
