using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.RelationalStorage;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>AC-AISQL-004/006: SQL wraps the actual persisted Orleans model operations and their canonical results.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlClusterModelParityTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcAisql006SqlAtomicCommandAndRetryExposeTheSameEventsAndNonconsumingQueue()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        var command = scenario.LinkedModelsCommand();
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var receipt = await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var runtimeRetry = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, deadline.Token));
        await SqlClusterProtocol.EqualAsync(receipt, runtimeRetry.Value);
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token));
        await VerifyStreamAsync(sdk, mcp, scenario, receipt.Token.Position, deadline.Token);
        await VerifyQueueAsync(sdk, mcp, scenario, deadline.Token);
    }

    [Test]
    public async Task AcAisql004SqlAndClientsShareTypedTableGraphVectorsAndSamples()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.LinkedModelsCommand(), deadline.Token));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var traverse = new TraverseRequest(scenario.Partition, RelationalSqlClusterTokens.Graph, scenario.First);
        var runtimeGraph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(traverse, deadline.Token));
        var sqlGraph = await SqlClusterProtocol.SdkAsync<global::KeyLoad.GraphTraversal>(sdk,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traverse), deadline.Token);
        var officialGraph = await SqlClusterProtocol.McpAsync<global::KeyLoad.GraphTraversal>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traverse), deadline.Token);
        await Assert.That(runtimeGraph.Vertices.Length).IsEqualTo(RelationalSqlClusterTokens.RootAndNeighbor);
        await Assert.That(runtimeGraph.Vertices).Contains(scenario.First);
        await Assert.That(runtimeGraph.Vertices).Contains(scenario.Second);
        await Assert.That(runtimeGraph.Edges).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(runtimeGraph, sqlGraph);
        await SqlClusterProtocol.EqualAsync(runtimeGraph, officialGraph);
        await VerifySearchAsync(sdk, mcp, scenario, deadline.Token);
        await VerifySamplesAsync(sdk, mcp, scenario, deadline.Token);
    }

    [Test]
    public async Task AcAisql005NoBodySqlCallUsesTheEmptyCanonicalEnvelope()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        var sql = SqlClusterProtocol.NoBodyCall(scenario.Partition, McpCallerTools.QueryCapabilities);
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryCapabilitiesAsync(deadline.Token));
        var actual = await SqlClusterProtocol.SdkAsync<QueryCapabilityManifest>(sdk, sql, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var official = await SqlClusterProtocol.McpAsync<QueryCapabilityManifest>(mcp, sql, deadline.Token);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, official);
        await Assert.That(actual.ReadOnly).IsTrue();
    }

    private static async Task VerifyStreamAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, long committedPosition, CancellationToken cancellationToken)
    {
        var request = scenario.ReadStream();
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, cancellationToken));
        var actual = await SqlClusterProtocol.SdkAsync<StreamPage>(sdk,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), cancellationToken);
        var official = await SqlClusterProtocol.McpAsync<StreamPage>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), cancellationToken);
        await Assert.That(expected.Events).HasSingleItem();
        await Assert.That(expected.Events[0].Data.EventId).IsEqualTo(RelationalSqlClusterTokens.EventId);
        await Assert.That(expected.Events[0].Revision).IsEqualTo(RelationalSqlClusterTokens.FirstEventRevision);
        await SqlClusterProtocol.EqualAsync(expected.Events, actual.Events);
        await SqlClusterProtocol.EqualAsync(expected.Events, official.Events);
        await SqlClusterProtocol.EqualAsync(expected.Head, actual.Head);
        await SqlClusterProtocol.EqualAsync(expected.Head, official.Head);
        await Assert.That(actual.Stream).IsEqualTo(expected.Stream);
        await Assert.That(official.Stream).IsEqualTo(expected.Stream);
        await Assert.That(actual.HasMore).IsEqualTo(expected.HasMore);
        await Assert.That(official.HasMore).IsEqualTo(expected.HasMore);
        await Assert.That(expected.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
    }

    private static async Task VerifyQueueAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, CancellationToken cancellationToken)
    {
        var request = scenario.Inspect();
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var actual = await SqlClusterProtocol.SdkAsync<MessageInspectionResult>(sdk,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, request), cancellationToken);
        var official = await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, request), cancellationToken);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, official);
        await Assert.That(actual.Message).IsNotNull();
        await Assert.That(actual.Message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Message!.Metadata.Attempts).IsEqualTo(RelationalSqlClusterTokens.NoResults);
        await Assert.That(actual.Message!.Metadata.LeaseOwner).IsNull();
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        await SqlClusterProtocol.EqualAsync(expected, after);
    }

    private static async Task VerifySearchAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, CancellationToken cancellationToken)
    {
        foreach (var request in new[] { scenario.Search(), new SearchRequest(scenario.Partition, RelationalSqlClusterTokens.Table,
            TextField: RelationalSqlClusterTokens.TitlePath, Text: RelationalSqlClusterTokens.FirstTitle) })
        {
            var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
            var actual = await SqlClusterProtocol.SdkAsync<RankedDocument[]>(sdk,
                SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.SearchExecute, request), cancellationToken);
            var official = await SqlClusterProtocol.McpAsync<RankedDocument[]>(mcp,
                SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.SearchExecute, request), cancellationToken);
            await Assert.That(expected).HasSingleItem();
            await Assert.That(expected[0].Document.Reference).IsEqualTo(scenario.First);
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await SqlClusterProtocol.EqualAsync(expected, official);
        }
    }

    private static async Task VerifySamplesAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, CancellationToken cancellationToken)
    {
        var request = new ReadSamplesRequest(scenario.Partition, RelationalSqlClusterTokens.SeriesSet, RelationalSqlClusterTokens.SeriesId,
            RelationalSqlClusterTokens.SampleAt, RelationalSqlClusterTokens.SampleAt);
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(request, cancellationToken));
        var actual = await SqlClusterProtocol.SdkAsync<SampleRecord[]>(sdk,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.SeriesRead, request), cancellationToken);
        var official = await SqlClusterProtocol.McpAsync<SampleRecord[]>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.SeriesRead, request), cancellationToken);
        await Assert.That(expected).HasSingleItem();
        await Assert.That(expected[0].Sample.EventId).IsEqualTo(RelationalSqlClusterTokens.SampleId);
        await Assert.That(expected[0].Sample.Value).IsEqualTo(RelationalSqlClusterTokens.SampleValue);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, official);
    }
}
