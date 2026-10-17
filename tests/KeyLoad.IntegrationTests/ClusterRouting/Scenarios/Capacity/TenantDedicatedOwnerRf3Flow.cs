using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantDedicatedOwnerRf3Flow
{
    private const long RevisionIncrement = 1;
    private const long InitialPosition = 0;
    private const long UncreatedDocumentRevision = 0;
    private const string HealthyDocument = "dedicated-owner-healthy";
    private const string HealthyJson = "{\"dedicatedOwner\":true}";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantDedicatedOwnerRf3State state, CancellationToken token)
    {
        await TenantDedicatedOwnerRf3Assertions.DeniedAsync(wave, state, token);
        await TenantDedicatedOwnerRf3Assertions.SiblingAsync(wave, state, state.SiblingPlacement.DirectoryRevision, token);
        await seed.VerifyAsync(token);
        var moved = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(state.Move, token));
        var directoryRevision = checked(state.Original.DirectoryRevision + RevisionIncrement);
        await TenantDedicatedOwnerRf3Assertions.DedicatedAsync(seed, state, moved, directoryRevision, token);
        await StableAsync(wave, seed, state, moved, directoryRevision, token);
        await ColdAsync(wave, seed, state, moved, directoryRevision, null, null, token);
        var command = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents, HealthyDocument,
            HealthyJson, ExpectedRevision: UncreatedDocumentRevision));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(moved.DestinationOwner.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(moved.PublishedPlacement!.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(InitialPosition);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt("putDocument", RelationalSqlRf3Tokens.Documents,
            HealthyDocument, RevisionIncrement), receipt.Mutations.Single());
        await HealthyAsync(seed, command, receipt, token);
        await ColdAsync(wave, seed, state, moved, directoryRevision, command, receipt, token);
    }

    private static async Task StableAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantDedicatedOwnerRf3State state, PartitionMoveResult moved, long directoryRevision, CancellationToken token)
    {
        await TenantDedicatedOwnerRf3Assertions.DedicatedAsync(seed, state, moved, directoryRevision, token);
        await NativeActivationMigrationRf3Ownership.RequirePhysicalAsync(state.Physical,
            await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, token));
        await TenantDedicatedOwnerRf3Assertions.DeniedAsync(wave, state, token);
        await TenantDedicatedOwnerRf3Assertions.SiblingAsync(wave, state, directoryRevision, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, state.Move, moved, token);
        await seed.VerifyAsync(token);
    }

    private static async Task ColdAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantDedicatedOwnerRf3State state, PartitionMoveResult moved, long directoryRevision,
        CommandRequest? healthy, CommitReceipt? receipt, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, state.Move, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await TenantDedicatedOwnerRf3Assertions.NoSiblingParentAsync(wave, state);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await seed.ReconnectAsync(wave, token);
        await StableAsync(wave, seed, state, moved, directoryRevision, token);
        if (healthy is not null && receipt is not null)
        {
            await HealthyAsync(seed, healthy, receipt, token);
        }
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, state.Move, token);
        await TenantDedicatedOwnerRf3Assertions.NoSiblingParentAsync(wave, state);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await seed.ReconnectAsync(wave, token);
    }

    private static async Task HealthyAsync(PartitionMovementPublicParentRf3Seed seed, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token)));
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Documents, HealthyDocument);
        var expected = new DocumentResult(reference, RevisionIncrement, HealthyJson, false, []);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                await sdk.GetAsync(reference, receipt.Token, token)));
        }
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
            await seed.Official.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, receipt.Token), token))).Value);
    }
}
