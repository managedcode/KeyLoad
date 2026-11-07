using KeyLoad.Server.Authorization.Contracts;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace KeyLoad.Server.Authorization.Diagnostics;

internal sealed class RuntimeLogPrivacyProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        var body = RequestFailureDiagnosticLog.ExportBody(data);
        data.CategoryName = HttpTelemetryPrivacyPolicy.Category(data.CategoryName);
        data.Body = body;
        data.FormattedMessage = body;
        (data.Attributes /* KLD0041: Clear all native OpenTelemetry object-valued log attributes before export so caller payloads and unknown labels cannot leave the process. */) = null;
        data.Exception = null;
        data.TraceState = null;
        data.EventId = new EventId(data.EventId.Id);
    }
}
