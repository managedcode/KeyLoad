using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.DocumentStorage.ProtectedDocumentRf3MovementProtocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class ProtectedDocumentRf3MovementFlow
{
    private const int NoFailures = 0;

    internal static async Task<ProtectedDocumentRf3MovementState> RunAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, CommandRequest command, CommitReceipt seed, PhysicalOwnerDirectoryV1 directory,
        AtomicPartitionPlacementResolution original, string deniedKey, CancellationToken token)
    {
        var failures = new List<Exception>();
        ProtectedDocumentRf3MovementState? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            result = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey,
                official => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey,
                    target => ExecuteAsync(wave, source, target, official.Mcp, command, seed, directory, original, deniedKey, token), token), token);
        }, failures);
        if (failures.Count != NoFailures)
        {
            ServerFailureObserver.Observe(wave.RetainRoots, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private static async Task<ProtectedDocumentRf3MovementState> ExecuteAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, RequestCqrsRf3Callers target, McpOfficialClient official, CommandRequest command, CommitReceipt seed,
        PhysicalOwnerDirectoryV1 directory, AtomicPartitionPlacementResolution original, string deniedKey, CancellationToken token)
    {
        var expectedSeed = new CommitReceipt(command.CommandId, seed.Token,
            [new(P.Put, ProtectedDocumentRf3Seed.Collection, ProtectedDocumentRf3Seed.Entity, P.DocumentRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await NativeActivationOriginalSessionConservation.RequireReceiptAsync(expectedSeed, seed);
        var destination = directory.Owners.Single(owner => owner.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner;
        var beforeSource = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var beforeTarget = await McpCallerAssertions.SdkSuccessAsync(await target.Sdk.StatusAsync(token));
        var request = new PartitionMoveRequest(Guid.NewGuid(), command.Partition, destination.PhysicalShardId,
            original.Revision, PartitionMoveMode.Transfer);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await source.MovePartitionAsync(request, token));
        var reference = new EntityRef(command.Partition, ProtectedDocumentRf3Seed.Collection, ProtectedDocumentRf3Seed.Entity);
        await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, P.DocumentRevision, ProtectedDocumentRf3Seed.Initial, false, []),
            await PublicReadAssertions.DocumentAsync(await target.Sdk.GetAsync(reference, token)));
        var targetCut = await McpCallerAssertions.SdkSuccessAsync(await target.Sdk.StatusAsync(token));
        var movement = new ProtectedDocumentRf3MovementState(request, actual, directory.ControlOwner, destination, targetCut);
        await ProtectedDocumentRf3MovementResult.RequireAsync(movement, original, seed, beforeSource, beforeTarget);
        await ProtectedDocumentRf3MovementResult.PlacementAsync(source, movement, original, token);
        await ProtectedDocumentRf3MovementReplay.RunAsync(wave, source, target.Sdk, official, movement, command, seed, deniedKey, token);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var targetAfter = await McpCallerAssertions.SdkSuccessAsync(await target.Sdk.StatusAsync(token));
        await Assert.That(sourceAfter.Incarnation).IsEqualTo(movement.Source.Incarnation);
        await Assert.That(targetAfter.Incarnation).IsEqualTo(movement.Target.Incarnation);
        await Assert.That(sourceAfter.Applied).IsGreaterThanOrEqualTo(actual.SourceCut);
        await Assert.That(targetAfter.Applied).IsGreaterThanOrEqualTo(actual.InstalledReceipt!.Position);
        return movement;
    }
}
