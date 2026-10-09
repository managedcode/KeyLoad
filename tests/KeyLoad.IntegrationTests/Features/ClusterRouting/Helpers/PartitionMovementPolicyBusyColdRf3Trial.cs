using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPolicyBusyColdRf3Trial
{
    private const long RequestedPolicyEpoch = 2;

    internal static async Task<PartitionMovementPublicParentRf3NativeCut[]> RequireAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut[] before,
        CancellationToken cancellationToken)
    {
        var principal = PartitionMovementPublicParentRf3Administrator.Definition(RequestedPolicyEpoch, false);
        var commandId = Guid.NewGuid();
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var root = new KeyLoadClient(http, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        var refusal = await root.ConfigurePrincipalAsync(commandId, principal, cancellationToken).ConfigureAwait(false);
        await Assert.That(refusal.IsFailed).IsTrue();
        await Assert.That(refusal.Value).IsNull();
        await Assert.That(refusal.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyBusyColdRf3Assertions.RequireAsync(wave, before, after, commandId, principal);
        await PartitionMovementObservedFailureColdRf3Assertions.RequireRetainedAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        return after;
    }
}
