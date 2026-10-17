namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ConnectionRf3Reply<T>(T Value, Guid? RequestId);
