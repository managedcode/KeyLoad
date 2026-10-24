namespace KeyLoad.UnitTests.Authorization;

internal sealed record AuthorizationSuppressionCapture(long Count, KeyValuePair<string, string?>[] Tags);
