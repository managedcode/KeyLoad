using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Revocation
{
    private const long OriginalPendingEpoch = 2;
    private const long RevokedEpoch = 3;
    private const long RepairedEpoch = 4;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, KeyLoadClient sourceRoot, KeyLoadClient targetRoot,
        string secret, ApiKeyRecord credential, PlacementIntentV1 original, Guid persistId,
        PersistPlacementIntentRequest persist, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var caller = await TenantCapacityPlacementRf3RevocationCaller.OpenAsync(wave, secret, token);
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(wave, seed, state, sourceRoot, targetRoot,
                caller, credential, original, persistId, persist, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, KeyLoadClient sourceRoot, KeyLoadClient targetRoot,
        TenantCapacityPlacementRf3RevocationCaller caller, ApiKeyRecord credential, PlacementIntentV1 original,
        Guid persistId, PersistPlacementIntentRequest persist, CancellationToken token)
    {
        await Assert.That(original.PolicyEpoch).IsEqualTo(OriginalPendingEpoch);
        var id = Guid.NewGuid();
        var request = new ApplyPlacementIntentRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            original.IntentId, original.Revision, state.Control);
        var bytes = await TenantCapacityPlacementRf3RevocationCold.PendingAsync(
            wave, seed, caller, original, persistId, originalRows: null, token);
        await TenantCapacityPlacementRf3Authorization.ConfigureAsync(sourceRoot, targetRoot,
            seed.Partition.TenantId, administrator: false, RevokedEpoch, credential, token);
        await TenantCapacityPlacementRf3RevocationAssertions.RefuseAsync(caller, id, request,
            ErrorCode.PermissionDenied, KeyLoad.Orleans.GrainRoutingProtocol.AdministrationRequired,
            TenantCapacityPlacementRf3RevocationAssertions.McpPermissionDenied, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        bytes = await TenantCapacityPlacementRf3RevocationCold.PendingAsync(wave, seed, caller, original, persistId, bytes, token);
        await TenantCapacityPlacementRf3Authorization.ConfigureAsync(sourceRoot, targetRoot,
            seed.Partition.TenantId, administrator: true, RepairedEpoch, credential, token);
        await TenantCapacityPlacementRf3RevocationAssertions.RefuseAsync(caller, id, request,
            ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch,
            TenantCapacityPlacementRf3RevocationAssertions.McpEpochInvalidated, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        await TenantCapacityPlacementRf3RevocationCold.PendingAsync(wave, seed, caller, original, persistId, bytes, token);
        await TenantCapacityPlacementRf3RevocationApply.RunAsync(wave, seed, state, caller,
            original, id, request, persistId, persist, bytes, token);
    }
}
