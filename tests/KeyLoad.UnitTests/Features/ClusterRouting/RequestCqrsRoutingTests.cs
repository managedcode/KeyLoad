namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
[NotInParallel]
internal sealed class RequestCqrsRoutingTests(RequestCqrsClusterFixture fixture)
{
    internal const string RootPrincipalId = "root";
    internal const string Collection = "request-cqrs-routes";
    internal const string DocumentJson = "{\"value\":42}";
    internal static readonly TimeSpan InvocationBound = TimeSpan.FromSeconds(30);

    [Test]
    public Task AcCrs001NativeRequestRoutesAStableBatchThroughItsCommandGrain()
        => RequestCqrsRoutingCases.AcCrs001NativeRequestRoutesAStableBatchThroughItsCommandGrain(fixture);

    [Test]
    public Task AcCrs003RevokedCommandFailsSafelyAndFollowingRequestSucceeds()
        => RequestCqrsRoutingCases.AcCrs003RevokedCommandFailsSafelyAndFollowingRequestSucceeds(fixture);

    [Test]
    public Task AcCrs006MissingAndForgedContextFailBeforeCommandEffects()
        => RequestCqrsRoutingCases.AcCrs006MissingAndForgedContextFailBeforeCommandEffects(fixture);
}
