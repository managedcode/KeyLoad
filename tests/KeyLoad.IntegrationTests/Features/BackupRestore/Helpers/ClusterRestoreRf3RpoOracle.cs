using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3RpoOracle
{
    private const string MutationKind = "putDocument";
    private const long FirstRevision = 1;
    private const string Label = "Actual source post-capture ACK/native applied boundary: ";

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, CommandRequest command,
        CommitReceipt actual, string documentId, CancellationToken cancellationToken)
    {
        var partition = originals.SelectMany(owner => owner.Cut.Partitions)
            .First(row => row.Roster.Partition == seed.Partition);
        var owner = originals.Single(row => row.Cut.Owner.PhysicalShardId == partition.Placement.PhysicalShardId);
        await Assert.That(actual.Token.Position).IsGreaterThan(owner.Cut.AppliedIndex);
        var expected = new CommitReceipt(command.CommandId, new(owner.Cut.Owner.Incarnation,
            seed.Partition.AtomicPartitionId, actual.Token.Position, partition.Placement.PlacementEpoch),
            [new MutationReceipt(MutationKind, KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3Tokens.Documents, documentId, FirstRevision)], DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(
            command, cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await seed.Official
            .CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source,
            sql, cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official,
            sql, cancellationToken).ConfigureAwait(false));
        await TestContext.Current!.OutputWriter.WriteLineAsync(Label + System.Text.Json.JsonSerializer.Serialize(
            new
            {
                Owner = owner.Cut.Owner,
                seed.Partition,
                CapturedStorePosition = owner.Cut.StorePosition,
                CapturedAppliedIndex = owner.Cut.AppliedIndex,
                ActualPostCaptureReceipt = actual
            }, JsonDefaults.Options));
    }
}
