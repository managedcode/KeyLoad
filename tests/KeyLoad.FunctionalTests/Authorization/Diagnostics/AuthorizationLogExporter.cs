using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class AuthorizationLogExporter(IOptions<AuthorizationTelemetryCaptureOptions> options)
    : BaseExporter<LogRecord>
{
    internal AuthorizationCaptureBank<AuthorizationLogCapture> Bank { get; } = new(options);
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var log in batch)
        {
            var scopes = new List<string>();
            log.ForEachScope(static (scope, target) => target.Add(scope.Scope?.ToString() ?? string.Empty), scopes);
            Bank.Add(new(log.CategoryName, log.Body, log.FormattedMessage, log.EventId.Id, log.EventId.Name,
                log.LogLevel, log.Timestamp, log.TraceId.ToHexString(), log.SpanId.ToHexString(), log.TraceState,
                log.Exception?.ToString(), log.Attributes?.Select(static x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString())).ToArray() ?? [],
                [.. scopes]));
        }
        return ExportResult.Success;
    }
}
