using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventVectorTopicRetentionRf3Routes
{
    internal static Task<CommandRequest> RefuseAsync(TwoRf3MembershipWave wave, string credential,
        EventVectorTopicRetentionRf3Seed seed, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, seed.Node, credential, async callers =>
        {
            var result = await callers.Sdk.CommitAsync(seed.Refused, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.ResourceExhausted.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
                seed.Refused, token), ErrorCode.ResourceExhausted, dispatched: true);
            return seed.Refused;
        }, token);

    internal static Task<CommitReceipt> CommitAsync(TwoRf3MembershipWave wave, string credential,
        EventVectorTopicRetentionRf3Seed seed, CommandRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, seed.Node, credential, async callers =>
        {
            var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(request, token));
            var official = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, request, token))).Value;
            await SqlRf3Protocol.EqualAsync(original, official);
            await Assert.That(original.CommandId).IsEqualTo(request.CommandId);
            await Assert.That(original.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
            await Assert.That(original.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            return original;
        }, token);
}
