using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Policy
{
    internal const long RevokedEpoch = 2;
    internal const long RepairedEpoch = 3;
    private const string AdministratorRequired = "Cluster administration is required.";

    internal static async Task ChangeAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PhysicalShardAuthorityBoundaryRf3Actor actor, KeyLoadClient source, KeyLoadClient target, PartitionMovementPublicParentRf3NativeCut[][] baseline,
        long epoch, bool administrator, CancellationToken token)
    {
        var expected = PhysicalShardAuthorityBoundaryRf3Actor.Definition(epoch, administrator);
        var sourceResult = await McpCallerAssertions.SdkSuccessAsync(await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
            source.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
        await SqlRf3Protocol.EqualAsync(expected, sourceResult);
        await PhysicalShardAuthorityBoundaryRf3Cut.RequireAndReopenAsync(wave, seeds, baseline, token);
        await actor.ReconnectAsync(wave, token);
        var targetResult = await McpCallerAssertions.SdkSuccessAsync(await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
            target.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
        await SqlRf3Protocol.EqualAsync(expected, targetResult);
        await PhysicalShardAuthorityBoundaryRf3Cut.RequireAndReopenAsync(wave, seeds, baseline, token);
        await actor.ReconnectAsync(wave, token);
    }

    internal static async Task DeniedAsync(ManagedCode.Communication.Result<PartitionMoveResult> actual)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, AdministratorRequired), actual.Problem);
    }
}
