namespace KeyLoad.FunctionalTests.Authorization;

internal sealed record AuthorizationTelemetryFailure(ErrorCode Code, string? Detail);
