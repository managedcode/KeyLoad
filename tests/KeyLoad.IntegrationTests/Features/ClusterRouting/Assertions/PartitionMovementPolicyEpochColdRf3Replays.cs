using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPolicyEpochColdRf3Replays
{
    internal static async Task RequireOldUsersDeniedAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        await Assert.That(seed.Originals.Count).IsGreaterThan(KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveProtocol.EmptyCount);
        foreach (var original in seed.Originals)
        {
            var sdk = await seed.Source.CommitAsync(original.Command, cancellationToken).ConfigureAwait(false);
            await Assert.That(sdk.IsFailed).IsTrue();
            await Assert.That(sdk.Value).IsNull();
            await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(McpCallerTools.DocumentsCommit,
                original.Command, cancellationToken).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
            var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, original.Command,
                original.Command.CommandId);
            var q1 = await seed.Source.ExecuteSqlAsync(sql, cancellationToken).ConfigureAwait(false);
            await Assert.That(q1.IsFailed).IsTrue();
            await Assert.That(q1.Value).IsNull();
            await Assert.That(q1.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(SqlOperationProtocol.ToolName,
                sql, cancellationToken).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
        }
    }
}
