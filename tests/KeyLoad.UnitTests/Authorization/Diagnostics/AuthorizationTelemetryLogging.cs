using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryLogging
{
    private static readonly Action<ILogger, string, Exception?> HealthyEvent = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(AuthorizationTelemetryTestProtocol.HealthyEventId),
        AuthorizationTelemetryTestProtocol.HealthyTemplate);
    private static readonly Action<ILogger, string?, Exception?> DeniedEvent = LoggerMessage.Define<string?>(
        LogLevel.Error, new EventId(AuthorizationTelemetryTestProtocol.DeniedEventId, AuthorizationTelemetryTestProtocol.ExceptionCanary),
        AuthorizationTelemetryTestProtocol.FailureTemplate);
    internal static void Healthy(ILogger logger, string payload) => HealthyEvent(logger, payload, null);
    internal static void Denied(ILogger logger, string? payload, Exception error) => DeniedEvent(logger, payload, error);
}
