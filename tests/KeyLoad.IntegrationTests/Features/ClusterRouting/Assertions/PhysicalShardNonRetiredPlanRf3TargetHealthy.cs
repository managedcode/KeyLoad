using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardNonRetiredPlanRf3TargetHealthy
{
    private const int FirstEntry = 0;
    private const long FirstRevision = 1;
    private const long EmptyPosition = 0;
    private const string DocumentId = "whole-partition-target-before-merge";
    private const string DocumentJson = "{\"targetBeforeMerge\":true}";
    private const string PutKind = "putDocument";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] requests, PartitionMoveResult[] terminals, CancellationToken token)
    {
        var commands = new CommandRequest[seeds.Length];
        var receipts = new CommitReceipt[seeds.Length];
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            var seed = seeds[index];
            commands[index] = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents, DocumentId, DocumentJson));
            receipts[index] = await McpCallerAssertions.SdkSuccessAsync(await OriginalAsync(
                seed.Source.CommitAsync(commands[index], token)));
            var actual = receipts[index];
            await Assert.That(actual.Token.Position).IsGreaterThan(EmptyPosition);
            await SqlRf3Protocol.EqualAsync(new CommitReceipt(commands[index].CommandId,
                new(terminals[index].DestinationOwner.Incarnation, seed.Partition.AtomicPartitionId,
                    actual.Token.Position, terminals[index].PublishedPlacement!.PlacementEpoch),
                [new(PutKind, RelationalSqlRf3Tokens.Documents, DocumentId, FirstRevision)],
                DurabilityProfile.QuorumProcessDurable), actual);
            await RequireCurrentAsync(seed, token);
            await ReplayAsync(seed, commands[index], actual, token);
        }
        var before = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, requests, token);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        foreach (var seed in seeds)
        {
            await seed.ReconnectAsync(wave, token);
        }
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await ReplayAsync(seeds[index], commands[index], receipts[index], token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var after = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, requests, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        foreach (var seed in seeds)
        {
            await seed.ReconnectAsync(wave, token);
        }
    }

    private static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await OriginalAsync(
            seed.Source.CommitAsync(command, token))));
        await RequireCurrentAsync(seed, token);
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await OriginalAsync(
            seed.Official.CallAsync(McpCallerTools.DocumentsCommit, command, token)))).Value);
        await RequireCurrentAsync(seed, token);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source, sql, token));
        await RequireCurrentAsync(seed, token);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official, sql, token));
        await RequireCurrentAsync(seed, token);
    }

    internal static async Task RequireCurrentAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        await seed.VerifyAsync(token);
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Documents, DocumentId);
        var expected = new DocumentResult(reference, FirstRevision, DocumentJson, false, []);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await OriginalAsync(
                sdk.GetAsync(reference, token))));
        }
        var request = new GetDocumentRequest(reference);
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(await OriginalAsync(
            seed.Official.CallAsync(McpCallerTools.DocumentsGet, request, token)))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsGet, request);
        await SqlRf3Protocol.EqualAsync(new DocumentReadResult(expected),
            await SqlRf3Protocol.SdkAsync<DocumentReadResult>(seed.Source, sql, token));
        await SqlRf3Protocol.EqualAsync(new DocumentReadResult(expected),
            await SqlRf3Protocol.McpAsync<DocumentReadResult>(seed.Official, sql, token));
    }

    private static async Task<T> OriginalAsync<T>(Task<T> original)
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => original, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return await original.ConfigureAwait(false);
    }
}
