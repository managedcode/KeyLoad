namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationSuppressionCapture(long Count, KeyValuePair<string, string?>[] Tags);
