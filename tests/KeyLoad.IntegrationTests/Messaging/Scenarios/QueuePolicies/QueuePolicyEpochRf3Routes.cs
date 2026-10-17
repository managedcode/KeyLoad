using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueuePolicyEpochRf3Routes
{
    internal static Task<CommitReceipt> CompleteAsync(TwoRf3MembershipWave wave, QueuePolicyEpochRf3Seed seed,
        DeliveryCommand command, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async callers =>
        {
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(command, token));
            await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Lane.Partition.AtomicPartitionId);
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value);
            var sql = SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesComplete, command);
            await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
            await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
            return receipt;
        }, token);

    internal static Task<bool> RefuseAsync(TwoRf3MembershipWave wave, QueuePolicyEpochRf3Seed seed,
        IEnumerable<DeliveryCommand> commands, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async callers =>
        {
            var before = await QueuePolicyEpochRf3Assertions.InspectAsync(callers.Sdk, seed, token);
            foreach (var command in commands)
            {
                token.ThrowIfCancellationRequested();
                await QueueLeaseRf3Assertions.DeniedAsync(await callers.Sdk.CompleteAsync(command, token), ErrorCode.PermissionDenied);
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete,
                    command, token), ErrorCode.PermissionDenied, dispatched: true);
                var sql = SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesComplete, command);
                await QueueLeaseRf3Assertions.DeniedAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.PermissionDenied);
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                    sql, token), ErrorCode.PermissionDenied, dispatched: true);
                await SqlRf3Protocol.EqualAsync(before, await QueuePolicyEpochRf3Assertions.InspectAsync(callers.Sdk, seed, token));
            }
            return true;
        }, token);

    internal static Task<PrincipalRecord> PolicyAsync(TwoRf3MembershipWave wave, string administrator,
        QueuePolicyEpochRf3Seed seed, PrincipalRecord original, Capability capabilities, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            administrator, async callers =>
        {
            var updated = original with
            {
                PolicyEpoch = checked(original.PolicyEpoch + 1),
                Grants = [.. original.Grants.Select(grant => grant.Resource == seed.Lane.Queue
                    ? grant with { Capabilities = capabilities } : grant)]
            };
            var actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigurePrincipalAsync(
                Guid.NewGuid(), updated, token));
            await SqlRf3Protocol.EqualAsync(updated, actual);
            return actual;
        }, token);
}
