using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.RecoveryTests.Features.EventStreams;
using static KeyLoad.IntegrationTests.Features.EventStreams.SharedDomainPublicProtocol;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicModels
{
    internal static Task AllAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
        => SharedDomainPublicSession.RunAsync(fixture, fixture.AdminKey, async (sdk, official) =>
        {
            foreach (var scope in state.Scopes)
            {
                await SharedDomainPublicDocuments.RequireAsync(sdk, official, state, scope, token);
                await SharedDomainPublicStreams.RequireAsync(sdk, official, state, scope, token);
                await SharedDomainPublicQueues.RequireAsync(sdk, official, state, scope, token);
                await OutboxAsync(sdk, official, state, scope, token);
            }
        }, token);

    private static async Task OutboxAsync(KeyLoadClient sdk, McpOfficialClient official, SharedDomainPublicState state,
        SharedDomainScope scope, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scope.Partition, token));
        var second = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await official.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(scope.Partition), token))).Value;
        await SharedDomainPublicValues.EqualAsync(second, actual);
        var processed = state.Processed && scope.Partition.TransactionDomainId == FirstDomain;
        var count = scope.Resources.Length + (processed ? ProducerEffects : NoRevision)
            + (state.HealthyWritten ? scope.Resources.Length : NoRevision);
        await Assert.That(actual.Head.Tail).IsEqualTo(count);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo(count);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(FirstRevision);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan(NoRevision);
        await Assert.That(actual.Consumers).IsEmpty();
        var key = scope.Partition.AtomicPartitionId;
        if (state.Outboxes.TryGetValue(key, out var previous) && previous.Head.Tail == count)
        {
            await SharedDomainPublicValues.EqualAsync(actual, previous);
        }
        state.Outboxes[key] = actual;
    }
}
