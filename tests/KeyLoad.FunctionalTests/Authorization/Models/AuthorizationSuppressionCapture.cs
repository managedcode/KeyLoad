namespace KeyLoad.FunctionalTests.Authorization;

internal sealed record AuthorizationSuppressionCapture(long Count, KeyValuePair<string, string?>[] Tags);
