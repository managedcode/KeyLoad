using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3Healthy
{
    private const int FirstEntry = 0;
    private const long FirstDocumentRevision = 1;
    private const string DocumentId = "whole-partition-final";
    private const string DocumentJson = "{\"settled\":true}";

    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] requests, PartitionMoveResult[] terminals, CancellationToken token)
    {
        var commands = new CommandRequest[seeds.Length];
        var receipts = new CommitReceipt[seeds.Length];
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            var seed = seeds[index];
            commands[index] = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents,
                DocumentId, DocumentJson));
            receipts[index] = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(
                commands[index], token).ConfigureAwait(false));
            await Assert.That(receipts[index].CommandId).IsEqualTo(commands[index].CommandId);
            await Assert.That(receipts[index].Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await Assert.That(receipts[index].Token.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
            await Assert.That(receipts[index].Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
            await Assert.That(receipts[index].Token.OwnershipEpoch).IsEqualTo(terminals[index].PublishedPlacement!.PlacementEpoch);
            await RequireResultAsync(seed, commands[index], receipts[index], token);
        }
        var before = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, requests, token);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        for (var index = FirstEntry; index < seeds.Length; index++)
        { await RequireResultAsync(seeds[index], commands[index], receipts[index], token); }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var after = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, requests, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
    }

    private static async Task RequireResultAsync(PartitionMovementPublicParentRf3Seed seed,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CommitAsync(command, token).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source, sql, token));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official, sql, token));
        await SqlRf3Protocol.EqualAsync(new DocumentResult(new(seed.Partition, RelationalSqlRf3Tokens.Documents,
            DocumentId), FirstDocumentRevision, DocumentJson, false, []), await PublicReadAssertions.DocumentAsync(
                await seed.Source.GetAsync(new(seed.Partition, RelationalSqlRf3Tokens.Documents, DocumentId), token)));
    }
}
