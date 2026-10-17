namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationMetricCapture(string Meter, string Name, long Count, double Sum,
    KeyValuePair<string, string?>[] Tags);
