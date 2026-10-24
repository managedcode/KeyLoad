using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.RelationalStorage;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

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
            DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.PersistedCopies).IsEqualTo(3);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
    }

    internal static async Task ModelsAsync(RequestCqrsClusterCallers callers,
        DeclarativeReverseQuotaState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.SeedReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Seed, token)));
        await SqlClusterProtocol.EqualAsync(state.SeedReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        await DeclarativeCompositionBudgetFlow.ColdAsync(callers, state.Scenario, state.Source, token);
        var traversal = new TraverseRequest(state.Scenario.Partition, DeclarativeReverseQuotaFlow.Graph, state.Scenario.First);
        await SqlClusterProtocol.EqualAsync(state.Source.Graph, await SqlClusterProtocol.McpAsync<global::KeyLoad.GraphTraversal>(
            callers.Mcp, SqlClusterProtocol.Call(state.Scenario.Partition, McpCallerTools.GraphTraverse, traversal), token));
        foreach (var (reference, json, original) in new[] {
            (state.Scenario.First, RelationalSqlClusterTokens.FirstRow, state.FirstDocument),
            (state.Scenario.Second, RelationalSqlClusterTokens.SecondRow, state.SecondDocument) })
        {
            var actual = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, token));
            await SqlClusterProtocol.EqualAsync(original, actual);
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Reference).IsEqualTo(reference);
            await Assert.That(actual.Json).IsEqualTo(json);
            await Assert.That(actual.Revision).IsEqualTo(InitialVersion);
            await Assert.That(actual.Redacted).IsFalse();
            await Assert.That(actual.RedactedFields).IsEmpty();
            await SqlClusterProtocol.EqualAsync(actual, (await PublicReadAssertions.DocumentAsync(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        foreach (var (edge, sequence) in new[] { (FirstEdge, FirstSequence), (SecondEdge, SecondSequence) })
        {
            await MessageAsync(callers, state, edge, sequence, token);
        }
    }

    private static async Task MessageAsync(RequestCqrsClusterCallers callers, DeclarativeReverseQuotaState state,
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
        var link = new QueueGraphLink(state.Scenario.First, state.Scenario.Second, RelationalSqlClusterTokens.EdgeLabel);
        var expected = new MessageInspection(new MessageMetadata(id, MessageState.Ready, NoAttempts,
            InitialVersion, sequence, null, null), DatabaseCompositionClusterPayload.OrdinalGolden(link), Headers);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, official);
    }
}
