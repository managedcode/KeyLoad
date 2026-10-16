using KeyLoad.Client;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using C = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireCorpus;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspirePublicAssertions
{
    internal static async Task CompleteAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveRequest request, PartitionMoveResult expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(
            await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName, request, token))).Value);
    }

    internal static async Task<ControlledRetireAspireHealthyState> HealthyAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var command = C.Healthy(seed.Partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch + C.Revision);
        var target = seed.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(target.Incarnation);
        await Assert.That(receipt.Token.Position).IsGreaterThan(C.Revision);
        await SqlRf3Protocol.EqualAsync<MutationReceipt[]>([new(C.PutKind, C.Collection, C.HealthyId, C.Revision)], receipt.Mutations.ToArray());
        var state = new ControlledRetireAspireHealthyState(command, receipt);
        await HealthyReplayAsync(seed, state, token);
        return state;
    }

    internal static async Task HealthyNativeAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireHealthyState state, IReadOnlyDictionary<string, NativeRetireCutWitness> cuts)
    {
        var target = seed.Directory.Owners.Single(owner =>
            owner.Owner.PhysicalShardId == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        var destinations = cuts.Values.Where(cut => cut.Identity.Incarnation == target.Incarnation).ToArray();
        await Assert.That(destinations.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var destination in destinations)
        {
            var entry = destination.ReceiptEntry ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
            await Assert.That(destination.CommittedIndex).IsGreaterThanOrEqualTo(entry.Index);
            var operation = entry.Operation ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.Batch);
            await Assert.That(operation.Id).IsEqualTo(state.Command.CommandId);
            await Assert.That(operation.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
            await SqlRf3Protocol.EqualAsync(state.Command, NativeCommandPayload.Read<CommandRequest>(operation));
            var expected = new CommitReceipt(C.HealthyCommandId,
                new(target.Incarnation, seed.Partition.AtomicPartitionId, entry.Index,
                    seed.OriginalPlacement.PlacementEpoch + C.Revision),
                [new(C.PutKind, C.Collection, C.HealthyId, C.Revision)], DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(expected, state.Receipt);
        }
    }

    internal static async Task HealthyReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireHealthyState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(state.Command, token)));
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        var reference = new EntityRef(seed.Partition, C.Collection, C.HealthyId);
        var expected = new DocumentResult(reference, C.Revision, C.HealthyJson, false, []);
        await SqlRf3Protocol.EqualAsync(expected,
            await PublicReadAssertions.DocumentAsync(await seed.Source.GetAsync(reference, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
            await seed.Official.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
    }
}
