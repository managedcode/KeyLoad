using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CurrentOwnershipTokenAfterMovementRf3Flow
{
    private const int FirstOriginal = 0;
    private const long RevisionIncrement = 1;
    private const long DocumentRevision = 1;
    private const long InitialPosition = 0;
    private const string CurrentDocumentId = "current-owner-token";
    private const string CurrentDocumentJson = "{\"currentOwner\":true}";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var original = seed.Originals[FirstOriginal].Receipt.Token;
        await Assert.That(original.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(original.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(original.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
        var expected = new DocumentResult(seed.Models.First, DocumentRevision, RelationalSqlRf3Tokens.FirstRow, false, []);
        await RequireReadAsync(seed, seed.Models.First, original, expected, token);
        var moved = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(
            seed.FirstRequest, token).ConfigureAwait(false));
        var publication = checked(seed.OriginalPlacement.DirectoryRevision + RevisionIncrement);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed, seed.FirstRequest, seed.OriginalPlacement,
            seed.Directory.ControlOwner, moved, publication, publication, token);
        await RequireInvalidAsync(seed, original, token);
        await seed.VerifyAsync(token);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await RequireInvalidAsync(seed, original, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest, moved, token);
        await seed.VerifyAsync(token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await RequireHealthyAsync(wave, seed, moved, original, token);
    }

    private static async Task RequireInvalidAsync(PartitionMovementPublicParentRf3Seed seed, CommitToken original,
        CancellationToken token)
    {
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var refusal = await sdk.GetAsync(seed.Models.First, original, token).ConfigureAwait(false);
            await Assert.That(refusal.IsSuccess).IsFalse();
            await Assert.That(refusal.Value).IsNull();
            await Assert.That(refusal.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        }
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(seed.Models.First, original), token).ConfigureAwait(false),
            ErrorCode.TokenInvalidated, dispatched: true);
    }

    private static async Task RequireReadAsync(PartitionMovementPublicParentRf3Seed seed, EntityRef reference,
        CommitToken minimum, DocumentResult expected, CancellationToken token)
    {
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
            await sdk.GetAsync(reference, minimum, token).ConfigureAwait(false)));
        }
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
            await seed.Official.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token)
                .ConfigureAwait(false))).Value);
    }

    private static async Task RequireHealthyAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult moved, CommitToken original, CancellationToken token)
    {
        var command = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents,
            CurrentDocumentId, CurrentDocumentJson));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.Position).IsGreaterThan(InitialPosition);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(moved.DestinationOwner.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(moved.PublishedPlacement!.PlacementEpoch);
        var expected = new DocumentResult(new(seed.Partition, RelationalSqlRf3Tokens.Documents, CurrentDocumentId),
            DocumentRevision, CurrentDocumentJson, false, []);
        await RequireReadAsync(seed, expected.Reference, receipt.Token, expected, token);
        await RequireReceiptAsync(seed, command, receipt, token);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await RequireInvalidAsync(seed, original, token);
        await RequireReadAsync(seed, expected.Reference, receipt.Token, expected, token);
        await RequireReceiptAsync(seed, command, receipt, token);
        await seed.VerifyAsync(token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
    }

    private static async Task RequireReceiptAsync(PartitionMovementPublicParentRf3Seed seed, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CommitAsync(command, token).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source, sql, token));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official, sql, token));
    }
}
