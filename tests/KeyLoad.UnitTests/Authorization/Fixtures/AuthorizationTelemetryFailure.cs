namespace KeyLoad.UnitTests.Authorization;

internal sealed record AuthorizationTelemetryFailure(ErrorCode Code, string? Detail);
