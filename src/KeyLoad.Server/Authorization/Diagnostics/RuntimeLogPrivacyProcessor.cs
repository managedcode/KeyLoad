using KeyLoad.Server.Features.Authorization.Contracts;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace KeyLoad.Server.Features.Authorization.Diagnostics;

internal sealed class RuntimeLogPrivacyProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        data.CategoryName = HttpTelemetryPrivacyPolicy.Category(data.CategoryName);
        data.Body = HttpTelemetryPrivacyPolicy.RuntimeBody;
        data.FormattedMessage = HttpTelemetryPrivacyPolicy.RuntimeBody;
        data.Attributes = null;
        data.Exception = null;
        data.TraceState = null;
        data.EventId = new EventId(data.EventId.Id);
    }
}
