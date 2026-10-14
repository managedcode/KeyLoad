using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Movement
{
    internal static async Task<(PartitionMoveRequest Request, PartitionMoveResult Result)> TransferAsync(
        TwoRf3MembershipWave wave, string administrator, RemoteAnnPublicationRf3State state, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, administrator, IntegrationClientOptions.Execution());
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(
            new(RemoteAnnPublicationRf3Protocol.Version, state.Scenario.Partition), token));
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        var target = directory.Owners.Single(entry => entry.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner;
        var request = new PartitionMoveRequest(Guid.NewGuid(), state.Scenario.Partition,
            target.PhysicalShardId, before.Revision, PartitionMoveMode.Transfer);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.MovePartitionAsync(request, token));
        var installed = actual.InstalledReceipt ?? throw new InvalidOperationException(RemoteAnnPublicationRf3Protocol.InvalidOwner);
        await Assert.That(installed.Position).IsGreaterThan(RemoteAnnPublicationRf3Protocol.EmptyCount);
        await Assert.That(installed.Incarnation).IsEqualTo(target.Incarnation);
        await Assert.That(installed.AtomicPartitionId).IsEqualTo(state.Scenario.Partition.AtomicPartitionId);
        await Assert.That(installed.OwnershipEpoch).IsEqualTo(checked(before.PlacementEpoch + RemoteAnnPublicationRf3Protocol.FirstEpoch));
        await Assert.That(actual.SourceCut).IsGreaterThan(RemoteAnnPublicationRf3Protocol.EmptyCount);
        var expected = new AtomicPartitionPlacementV1(RemoteAnnPublicationRf3Protocol.Version, state.Scenario.Partition,
            target.PhysicalShardId, checked(before.Revision + RemoteAnnPublicationRf3Protocol.FirstEpoch),
            target.Incarnation, target.VoterIds, checked(before.PlacementEpoch + RemoteAnnPublicationRf3Protocol.FirstEpoch));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(new PartitionMoveResult(request.MoveId,
            state.Scenario.Partition, PartitionMovePhase.Retired, directory.ControlOwner, target,
            actual.SourceCut, installed, expected), actual);
        var wrong = await sdk.MaintainAnnIndexAsync(state.Original with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        }, token);
        await Assert.That(wrong.IsSuccess).IsFalse();
        await Assert.That(wrong.Value).IsNull();
        await Assert.That(wrong.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        return (request, actual);
    }

    internal static async Task ReplayAsync(TwoRf3MembershipWave wave, string administrator,
        PartitionMoveRequest request, PartitionMoveResult original, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, administrator, IntegrationClientOptions.Execution());
        await using var official = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, administrator, token);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.MovePartitionAsync(request, token)));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(original,
            (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(await official.CallAsync(
                PartitionMovePublicProtocol.ToolName, request, token))).Value);
    }
}
