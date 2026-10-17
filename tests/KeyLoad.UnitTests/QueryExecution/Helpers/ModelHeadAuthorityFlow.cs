using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ModelHeadAuthorityFlow
{
    internal static async Task RunAsync(int fault, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new ModelHeadAuthorityFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                await DeniedAsync(fixture, token);
                await fixture.PolicyAsync(true, 2L);
                await ModelHeadAuthorityOperations.HealthyAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
                await ModelHeadAuthorityOperations.RefuseAsync(fixture.Harness.Database, fixture.Harness.Store, fixture.Harness.Partition,
                    ModelHeadAuthorityFixture.Reader, ErrorCode.TokenInvalidated, "The event source generation is stale.", 2L, token);
                await ModelHeadAuthorityFault.RunAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, fault, token);
                await fixture.PolicyAsync(false, 3L);
                await DeniedAsync(fixture, token);
                await fixture.PolicyAsync(true, 4L);
                await ModelHeadAuthorityOperations.HealthyAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
                await ModelHeadAuthorityCold.RunAsync(fixture, fault, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task DeniedAsync(ModelHeadAuthorityFixture fixture, CancellationToken token)
        => ModelHeadAuthorityOperations.RefuseAsync(fixture.Harness.Database, fixture.Harness.Store, fixture.Harness.Partition,
            ModelHeadAuthorityFixture.Reader, ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.", 1L, token);
}
