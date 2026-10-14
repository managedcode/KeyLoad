using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class TypedEndpointJoinReverseAssertions
{
    private const string Prefix = "typed-reverse-";
    private const string ChangedPrefix = "typed-reverse-conflict-";
    private const string RepairedPrefix = "typed-reverse-repaired-";
    private const string EnqueueKind = "enqueue";
    private const long InitialVersion = 1;
    private const long InitialReadySequence = 1;
    private const long RepairedReadySequence = 2;
    private const int NoAttempts = 0;

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt actual, bool repaired = false)
    {
        var id = (repaired ? RepairedPrefix : Prefix) + TypedEndpointJoinProtocol.Prefix + TypedEndpointJoinProtocol.MatchedMessage;
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new(EnqueueKind, TypedEndpointJoinProtocol.EmptyQueue, id, InitialVersion)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
    }

    internal static Task<bool> OriginalAsync(ClusterFixture fixture, RequestCqrsRf3Callers admin,
        TypedEndpointJoinState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reverse!.Identity.Secret,
            async callers =>
    {
        var reverse = state.Reverse!;
        await SqlRf3Protocol.EqualAsync(reverse.DeniedProblem,
            await TypedEndpointJoinReverseFlow.FencedAsync(callers, reverse.Denied, token));
        if (reverse.Current is { } current && reverse.CurrentReceipt is { } currentReceipt)
        {
            await SqlRf3Protocol.EqualAsync(reverse.FencedProblem!.Value,
                await TypedEndpointJoinReverseFlow.FencedAsync(callers, reverse.Command, token));
            await ReplayAsync(callers, current, currentReceipt, repaired: true, token);
        }
        else
        {
            await ReplayAsync(callers, reverse.Command, reverse.Receipt, repaired: false, token);
        }
        await ReceiptAsync(reverse.Command, reverse.Receipt);
        await UnchangedAsync(admin, state, reverse, token);
        return true;
    }, token);

    private static async Task ReplayAsync(RequestCqrsRf3Callers callers, CommandRequest command,
        CommitReceipt receipt, bool repaired, CancellationToken token)
    {
        var sql = DeclarativeCompositionSql.Reverse(command, (GraphToQueueMutation)command.Mutations[0]);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token)));
        await ReceiptAsync(command, receipt, repaired);
    }

    internal static async Task UnchangedAsync(RequestCqrsRf3Callers admin, TypedEndpointJoinState state,
        TypedEndpointJoinReverseState? reverse, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Models,
            await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
        var suffix = TypedEndpointJoinProtocol.Prefix + TypedEndpointJoinProtocol.MatchedMessage;
        var changed = new InspectMessageRequest(new(state.Seed.Partition, TypedEndpointJoinProtocol.EmptyQueue),
            ChangedPrefix + suffix);
        await Assert.That(await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(changed, token))).IsNull();
        await Assert.That((await PublicReadAssertions.MessageAsync(
            await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, changed, token))).Value).IsNull();
        await MessageAsync(admin, state, Prefix + suffix, InitialReadySequence, reverse is not null, token);
        await MessageAsync(admin, state, RepairedPrefix + suffix, RepairedReadySequence, reverse?.Current is not null, token);
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers admin, TypedEndpointJoinState state,
        string id, long sequence, bool exists, CancellationToken token)
    {
        var request = new InspectMessageRequest(new(state.Seed.Partition, TypedEndpointJoinProtocol.EmptyQueue), id);
        var actual = await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(request, token));
        var official = (await PublicReadAssertions.MessageAsync(
            await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value;
        if (!exists)
        {
            await Assert.That(actual).IsNull();
            await Assert.That(official).IsNull();
            return;
        }
        var link = new QueueGraphLink(TypedEndpointJoinProtocol.From(state.Seed.Partition),
            TypedEndpointJoinProtocol.To(state.Seed.Partition), TypedEndpointJoinProtocol.Label, TypedEndpointJoinProtocol.Attributes);
        var expected = new MessageInspection(new MessageMetadata(id, MessageState.Ready, NoAttempts,
            InitialVersion, sequence, null, null),
            DatabaseCompositionRf3Payload.OrdinalGolden(link), TypedEndpointJoinProtocol.Attributes);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, official);
    }
}
