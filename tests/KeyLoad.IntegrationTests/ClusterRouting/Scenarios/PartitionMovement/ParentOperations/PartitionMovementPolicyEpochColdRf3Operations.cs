using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPolicyEpochColdRf3Operations
{
    internal const long DemotedEpoch = 2;
    internal const long RestoredEpoch = 3;

    internal static Task DemoteAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
        => ConfigureAsync(wave, DemotedEpoch, false, cancellationToken);

    internal static Task RestoreAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
        => ConfigureAsync(wave, RestoredEpoch, true, cancellationToken);

    private static async Task ConfigureAsync(TwoRf3MembershipWave wave, long epoch, bool administrator,
        CancellationToken cancellationToken)
    {
        var expected = PartitionMovementPublicParentRf3Administrator.Definition(epoch, administrator);
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            using var http = McpCallerHttp.Create(wave.Application, node);
            var root = new KeyLoadClient(http, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(),
                expected, cancellationToken).ConfigureAwait(false));
            await SqlRf3Protocol.EqualAsync(expected, actual);
        }
    }

    internal static async Task<AtomicPartitionPlacementResolution> PlacementAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(PartitionMoveProtocol.Version, seed.Partition), cancellationToken).ConfigureAwait(false));

    internal static async Task<PartitionMoveResult> TransferAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveRequest request, AtomicPartitionPlacementResolution before, PhysicalShardRecord source,
        CancellationToken cancellationToken)
    {
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(request,
            cancellationToken).ConfigureAwait(false));
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, request, before, source,
            terminal, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, request, terminal,
            cancellationToken).ConfigureAwait(false);
        return terminal;
    }
}
