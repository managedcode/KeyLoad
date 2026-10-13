using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionOutcome
{
    private const long OriginalEpoch = 1;
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, StoredOutcome outcome, bool failed)
    {
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(OriginalEpoch);
        await Assert.That(outcome.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(capacity.Control);
        if (failed)
        {
            await Assert.That(outcome.Result.NativeValue).IsNull();
            await SqlRf3Protocol.EqualAsync(new OperationResult(null, ErrorCode.Conflict,
                TenantPlacementPolicyProtocol.Stale), outcome.Result);
        }
        else
        {
            await Assert.That(outcome.Result.Error).IsNull();
            await Assert.That(outcome.Result.NativeValue).IsNotNull();
        }
    }
}
