namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationTelemetryFailure(ErrorCode Code, string? Detail);
