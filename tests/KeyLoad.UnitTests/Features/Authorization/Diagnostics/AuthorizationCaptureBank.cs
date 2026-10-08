using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationCaptureBank<T>(IOptions<AuthorizationTelemetryCaptureOptions> options)
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<T> records = [];
    private bool truncated;
    internal void Add(T record)
    {
        lock (gate)
        {
            if (records.Count == options.Value.MaximumRecords)
            { truncated = true; return; }
            records.Add(record);
        }
    }
    internal T[] Snapshot()
    {
        lock (gate)
        {
            if (truncated)
            { throw new InvalidOperationException(AuthorizationTelemetryTestProtocol.CaptureFailure); }
            return [.. records];
        }
    }
}
