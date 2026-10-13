using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class DeclarativeReverseQuotaAssertions
{
    private const string FirstEdge = "bounded-a";
    private const string SecondEdge = "bounded-b";
    private const string EnqueueKind = "enqueue";
    private const long FirstSequence = 2;
    private const long SecondSequence = 3;
    private const long InitialVersion = 1;
    private const int NoAttempts = 0;
    private const string Headers = "{}";

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new(EnqueueKind, DeclarativeReverseQuotaFlow.Target, DeclarativeReverseQuotaFlow.Prefix + FirstEdge, InitialVersion),
             new(EnqueueKind, DeclarativeReverseQuotaFlow.Target, DeclarativeReverseQuotaFlow.Prefix + SecondEdge, InitialVersion)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
    }

    internal static async Task ModelsAsync(RequestCqrsRf3Callers callers,
        DeclarativeReverseQuotaState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.SeedReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Seed, token)));
        await SqlRf3Protocol.EqualAsync(state.SeedReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        await DeclarativeCompositionBudgetFlow.ColdAsync(callers, state.Scenario, state.Source, token);
        var traversal = new TraverseRequest(state.Scenario.Partition, DeclarativeReverseQuotaFlow.Graph, state.Scenario.First);
        await SqlRf3Protocol.EqualAsync(state.Source.Graph, await SqlRf3Protocol.McpAsync<global::KeyLoad.GraphTraversal>(
            callers.Mcp, SqlRf3Protocol.Call(state.Scenario.Partition, McpCallerTools.GraphTraverse, traversal), token));
        foreach (var (reference, json, original) in new[] {
            (state.Scenario.First, RelationalSqlRf3Tokens.FirstRow, state.FirstDocument),
            (state.Scenario.Second, RelationalSqlRf3Tokens.SecondRow, state.SecondDocument) })
        {
            var actual = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, token));
            await SqlRf3Protocol.EqualAsync(original, actual);
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Reference).IsEqualTo(reference);
            await Assert.That(actual.Json).IsEqualTo(json);
            await Assert.That(actual.Revision).IsEqualTo(InitialVersion);
            await Assert.That(actual.Redacted).IsFalse();
            await Assert.That(actual.RedactedFields).IsEmpty();
            await SqlRf3Protocol.EqualAsync(actual, (await PublicReadAssertions.DocumentAsync(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        foreach (var (edge, sequence) in new[] { (FirstEdge, FirstSequence), (SecondEdge, SecondSequence) })
        { await MessageAsync(callers, state, edge, sequence, token); }
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers callers, DeclarativeReverseQuotaState state,
        string edge, long sequence, CancellationToken token)
    {
        var id = DeclarativeReverseQuotaFlow.Prefix + edge;
        var request = new InspectMessageRequest(new(state.Scenario.Partition, DeclarativeReverseQuotaFlow.Target), id);
        var actual = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token));
        var official = (await PublicReadAssertions.MessageAsync(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value;
        if (state.Healthy is null)
        {
            await Assert.That(actual).IsNull();
            await Assert.That(official).IsNull();
            return;
        }
        var link = new QueueGraphLink(state.Scenario.First, state.Scenario.Second, RelationalSqlRf3Tokens.EdgeLabel);
        var expected = new MessageInspection(new MessageMetadata(id, MessageState.Ready, NoAttempts,
            InitialVersion, sequence, null, null), DatabaseCompositionRf3Payload.OrdinalGolden(link), Headers);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, official);
    }
}
