using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingPublicPolicy
{
    internal const int Version = 1;
    internal const long EmptyPosition = 0;
    internal const long InitialRevision = 1;
    internal const long HealthyRevision = 2;
    internal const long FollowingRevision = 3;
    private const long SourceReducedEpoch = 2;
    private const long TargetReducedEpoch = 3;
    private const long FirstRepairEpoch = 4;
    private const long RevokedEpoch = 5;
    private const long FinalRepairEpoch = 6;

    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient administrator, RequestCqrsClusterCallers callers,
        GraphIncomingClusterSeed seed, CancellationToken token)
    {
        var original = seed.Reader.Principal;
        var sourceReduced = original with
        {
            PolicyEpoch = SourceReducedEpoch,
            Grants = seed.TargetOnlyReader.Principal.Grants
        };
        await ConfigureAsync(administrator, sourceReduced, token);
        await UnchangedAsync(fixture, administrator, callers, seed, null, sourceHidden: true, token);
        var targetReduced = original with
        {
            PolicyEpoch = TargetReducedEpoch,
            Grants = [.. original.Grants.Where(
            grant => grant.Database != seed.TargetPartition.DatabaseId || grant.Resource != GraphIncomingClusterScenario.Collection)]
        };
        await ConfigureAsync(administrator, targetReduced, token);
        await UnchangedAsync(fixture, administrator, callers, seed, ErrorCode.NotFound, sourceHidden: false, token);
        await ConfigureAsync(administrator, original with
        {
            PolicyEpoch = FirstRepairEpoch
        }, token);
        await UnchangedAsync(fixture, administrator, callers, seed, null, sourceHidden: false, token);
        await ConfigureAsync(administrator, original with
        {
            PolicyEpoch = RevokedEpoch,
            Revoked = true
        }, token);
        await UnchangedAsync(fixture, administrator, callers, seed, ErrorCode.Unauthenticated, sourceHidden: false, token);
        await ConfigureAsync(administrator, original with
        {
            PolicyEpoch = FinalRepairEpoch
        }, token);
        await UnchangedAsync(fixture, administrator, callers, seed, null, sourceHidden: false, token);
    }

    private static async Task ConfigureAsync(KeyLoadClient administrator, PrincipalRecord principal, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));

    private static async Task UnchangedAsync(ClusterFixture fixture, KeyLoadClient administrator, RequestCqrsClusterCallers callers,
        GraphIncomingClusterSeed seed, ErrorCode? code, bool sourceHidden, CancellationToken token)
    {
        var before = await GraphIncomingPublicImages.CaptureAsync(administrator, seed, token);
        if (code is { } failure)
        {
            await GraphIncomingPublicRoutes.DeniedAsync(fixture, callers, seed, failure, token);
        }
        else
        {
            await GraphIncomingPublicRoutes.HealthyAsync(callers, seed, sourceHidden, token);
        }
        await GraphIncomingPublicImages.SameAsync(before,
            await GraphIncomingPublicImages.CaptureAsync(administrator, seed, token));
    }
}
