using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

/// <summary>Keeps bounded measurements tied to one observed node and process.</summary>
/// <param name="execution">Validated retained-history bounds.</param>
internal sealed class AdminMeasurements(IOptions<AdminConsoleOptions> execution)
{
    private readonly AdminConsoleOptions options = execution.Value;
    private const int EmptyCount = 0;
    private const int LatestOffset = 1;
    private const double Percent = 100;
    private AdminNodeSnapshot? previous;
    internal List<double> Rates { get; } = [];
    internal double? Latency { get; private set; }
    internal double? ErrorPercent { get; private set; }
    internal double? Rate => Rates.Count == EmptyCount ? null : Rates[^LatestOffset];

    internal void Clear()
    {
        previous = null;
        Rates.Clear();
        Latency = null;
        ErrorPercent = null;
    }

    internal void Observe(AdminNodeSnapshot next)
    {
        if (previous is not { } before || before.Node.NodeId != next.Node.NodeId
            || before.Http.ProcessInstance != next.Http.ProcessInstance || next.CapturedAt <= before.CapturedAt
            || next.Http.CompletedRequests < before.Http.CompletedRequests || next.Http.FailedRequests < before.Http.FailedRequests
            || next.Http.ElapsedMilliseconds < before.Http.ElapsedMilliseconds)
        {
            Clear();
            previous = next;
            return;
        }
        var completed = next.Http.CompletedRequests - before.Http.CompletedRequests;
        Rates.Add(completed / (next.CapturedAt - before.CapturedAt).TotalSeconds);
        if (Rates.Count > options.HistoryLimit)
        {
            Rates.RemoveAt(EmptyCount);
        }
        Latency = completed == EmptyCount ? null : (next.Http.ElapsedMilliseconds - before.Http.ElapsedMilliseconds) / completed;
        ErrorPercent = completed == EmptyCount ? null : Percent * (next.Http.FailedRequests - before.Http.FailedRequests) / completed;
        previous = next;
    }
}
