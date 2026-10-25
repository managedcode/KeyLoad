using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.RecoveryTests.EventStreams;
using static KeyLoad.IntegrationTests.EventStreams.SharedDomainPublicProtocol;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class SharedDomainPublicProcessing
{
    internal static async Task RunAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
    {
        var scope = state.Scopes[FirstIndex];
        await SharedDomainPublicSession.RunAsync(fixture, state.Identity.Secret, async (sdk, official) =>
        {
            var before = TimeProvider.System.GetUtcNow();
            var request = new ReceiveRequest(Guid.NewGuid(), new(scope.Partition, scope.Queue!), ReceiveCount, ReceiveBytes, LeaseSeconds);
            var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
            var after = TimeProvider.System.GetUtcNow();
            await ReceivedAsync(state, request, received, before, after);
            state.Delivery = received.Deliveries.Single();
            await SharedDomainPublicValues.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(await official.CallAsync(
                McpCallerTools.MessagesReceive, request, token))).Value, received);
            await SharedDomainPublicModels.AllAsync(fixture, state, token);
            var processing = SharedDomainCorpus.Processing(scope, state.Delivery.Token, Guid.NewGuid());
            await ForeignAsync(fixture, sdk, official, state, processing, token);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitProcessingAsync(processing, token));
            await SharedDomainPublicValues.ProcessingAsync(state, processing, receipt);
            state.Processing = processing;
            state.ProcessingReceipt = receipt;
            state.Processed = true;
            await ReplayAsync(sdk, official, state, token);
        }, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
        await SharedDomainPublicProducer.ReplayAllAsync(fixture, state, token);
    }

    private static async Task ReceivedAsync(SharedDomainPublicState state, ReceiveRequest request,
        ReceiveResult result, DateTimeOffset before, DateTimeOffset after)
    {
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Deliveries.Length).IsEqualTo(ReceiveCount);
        var delivery = result.Deliveries.Single();
        await Assert.That(string.IsNullOrWhiteSpace(delivery.Token)).IsFalse();
        await Assert.That(delivery.LeaseUntil).IsGreaterThanOrEqualTo(before.AddSeconds(LeaseSeconds));
        await Assert.That(delivery.LeaseUntil).IsLessThanOrEqualTo(after.AddSeconds(LeaseSeconds));
        await SharedDomainPublicValues.EqualAsync(delivery, new Delivery(Item, state.Scopes[FirstIndex].Payload,
            Headers, delivery.Token, FirstRevision, delivery.LeaseUntil, FirstAttempt, Generation));
        await Assert.That(result.Token.Position).IsGreaterThan(state.Originals[FirstIndex].Receipt.Token.Position);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(request.Lane.Partition.AtomicPartitionId);
    }

    private static async Task ForeignAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient official,
        SharedDomainPublicState state, ProcessingRequest processing, CancellationToken token)
    {
        var foreign = state.Scopes[SecondIndex];
        var wrongLane = SharedDomainCorpus.Processing(foreign, processing.Token, Guid.NewGuid());
        await RejectAsync(sdk, official, wrongLane, ErrorCode.TokenInvalidated, TokenScopeDetail, token);
        var wrongEffect = processing with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [.. processing.Effects, new EnqueueMessage(foreign.Queue!, Refused, foreign.Payload, Headers)]
        };
        await RejectAsync(sdk, official, wrongEffect, ErrorCode.Conflict, DomainDetail, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
    }

    private static async Task RejectAsync(KeyLoadClient sdk, McpOfficialClient official, ProcessingRequest request,
        ErrorCode code, string detail, CancellationToken token)
    {
        var first = await sdk.CommitProcessingAsync(request, token);
        await Assert.That(first.IsFailed).IsTrue();
        await Assert.That(first.Value).IsNull();
        await Assert.That(first.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(first.Problem.Detail).IsEqualTo(detail);
        _ = (await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.MessagesProcess, request, token), code, dispatched: true)).Value;
        var replay = await sdk.CommitProcessingAsync(request, token);
        await Assert.That(replay.IsFailed).IsTrue();
        await SharedDomainPublicValues.EqualAsync(replay.Problem, first.Problem);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient official, SharedDomainPublicState state, CancellationToken token)
    {
        var request = state.Processing ?? throw new InvalidOperationException(MissingReceipt);
        if (state.Fenced)
        {
            await RejectAsync(sdk, official, request, ErrorCode.PermissionDenied, EpochDetail, token);
            return;
        }
        var receipt = state.ProcessingReceipt ?? throw new InvalidOperationException(MissingReceipt);
        await SharedDomainPublicValues.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitProcessingAsync(request, token)), receipt);
        await SharedDomainPublicValues.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(
            McpCallerTools.MessagesProcess, request, token))).Value, receipt);
    }
}
