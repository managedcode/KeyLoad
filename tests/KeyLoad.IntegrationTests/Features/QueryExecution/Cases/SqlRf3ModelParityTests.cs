using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-AISQL-004/006: SQL wraps the actual persisted RF3 model operations and their canonical results.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlRf3ModelParityTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcAisql006SqlAtomicCommandAndNativeRetryExposeTheSameEventsAndNonconsumingQueue()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        var command = scenario.LinkedModelsCommand();
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var nativeRetry = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, deadline.Token));
        await SqlRf3Protocol.EqualAsync(receipt, nativeRetry.Value);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token));
        await VerifyStreamAsync(sdk, mcp, scenario, receipt.Token.Position, deadline.Token);
        await VerifyQueueAsync(sdk, mcp, scenario, deadline.Token);
    }

    [Test]
    public async Task AcAisql004SqlAndNativeClientsShareTypedTableGraphVectorsAndSamples()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.LinkedModelsCommand(), deadline.Token));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var traverse = new TraverseRequest(scenario.Partition, RelationalSqlRf3Tokens.Graph, scenario.First);
        var nativeGraph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(traverse, deadline.Token));
        var sqlGraph = await SqlRf3Protocol.SdkAsync<global::KeyLoad.GraphTraversal>(sdk,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traverse), deadline.Token);
        var officialGraph = await SqlRf3Protocol.McpAsync<global::KeyLoad.GraphTraversal>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traverse), deadline.Token);
        await Assert.That(nativeGraph.Vertices.Length).IsEqualTo(RelationalSqlRf3Tokens.RootAndNeighbor);
        await Assert.That(nativeGraph.Vertices).Contains(scenario.First);
        await Assert.That(nativeGraph.Vertices).Contains(scenario.Second);
        await Assert.That(nativeGraph.Edges).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(nativeGraph, sqlGraph);
        await SqlRf3Protocol.EqualAsync(nativeGraph, officialGraph);
        await VerifySearchAsync(sdk, mcp, scenario, deadline.Token);
        await VerifySamplesAsync(sdk, mcp, scenario, deadline.Token);
    }

    [Test]
    public async Task AcAisql005NoBodySqlCallUsesTheEmptyCanonicalEnvelope()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        var sql = SqlRf3Protocol.NoBodyCall(scenario.Partition, McpCallerTools.QueryCapabilities);
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryCapabilitiesAsync(deadline.Token));
        var actual = await SqlRf3Protocol.SdkAsync<QueryCapabilityManifest>(sdk, sql, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var official = await SqlRf3Protocol.McpAsync<QueryCapabilityManifest>(mcp, sql, deadline.Token);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, official);
        await Assert.That(actual.ReadOnly).IsTrue();
    }

    private static async Task VerifyStreamAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, long committedPosition, CancellationToken cancellationToken)
    {
        var request = scenario.ReadStream();
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, cancellationToken));
        var actual = await SqlRf3Protocol.SdkAsync<StreamPage>(sdk,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), cancellationToken);
        var official = await SqlRf3Protocol.McpAsync<StreamPage>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), cancellationToken);
        await Assert.That(expected.Events).HasSingleItem();
        await Assert.That(expected.Events[0].Data.EventId).IsEqualTo(RelationalSqlRf3Tokens.EventId);
        await Assert.That(expected.Events[0].Revision).IsEqualTo(RelationalSqlRf3Tokens.FirstEventRevision);
        await SqlRf3Protocol.EqualAsync(expected.Events, actual.Events);
        await SqlRf3Protocol.EqualAsync(expected.Events, official.Events);
        await SqlRf3Protocol.EqualAsync(expected.Head, actual.Head);
        await SqlRf3Protocol.EqualAsync(expected.Head, official.Head);
        await Assert.That(actual.Stream).IsEqualTo(expected.Stream);
        await Assert.That(official.Stream).IsEqualTo(expected.Stream);
        await Assert.That(actual.HasMore).IsEqualTo(expected.HasMore);
        await Assert.That(official.HasMore).IsEqualTo(expected.HasMore);
        await Assert.That(expected.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
    }

    private static async Task VerifyQueueAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var request = scenario.Inspect();
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var actual = await SqlRf3Protocol.SdkAsync<MessageInspection>(sdk,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, request), cancellationToken);
        var official = await SqlRf3Protocol.McpAsync<MessageInspection>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, request), cancellationToken);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, official);
        await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(actual.Metadata.LeaseOwner).IsNull();
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        await SqlRf3Protocol.EqualAsync(expected, after);
    }

    private static async Task VerifySearchAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, CancellationToken cancellationToken)
    {
        foreach (var request in new[] { scenario.Search(), new SearchRequest(scenario.Partition, RelationalSqlRf3Tokens.Table,
            TextField: RelationalSqlRf3Tokens.TitlePath, Text: RelationalSqlRf3Tokens.FirstTitle) })
        {
            var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
            var actual = await SqlRf3Protocol.SdkAsync<RankedDocument[]>(sdk,
                SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.SearchExecute, request), cancellationToken);
            var official = await SqlRf3Protocol.McpAsync<RankedDocument[]>(mcp,
                SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.SearchExecute, request), cancellationToken);
            await Assert.That(expected).HasSingleItem();
            await Assert.That(expected[0].Document.Reference).IsEqualTo(scenario.First);
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await SqlRf3Protocol.EqualAsync(expected, official);
        }
    }

    private static async Task VerifySamplesAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var request = new ReadSamplesRequest(scenario.Partition, RelationalSqlRf3Tokens.SeriesSet, RelationalSqlRf3Tokens.SeriesId,
            RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleAt);
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(request, cancellationToken));
        var actual = await SqlRf3Protocol.SdkAsync<SampleRecord[]>(sdk,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.SeriesRead, request), cancellationToken);
        var official = await SqlRf3Protocol.McpAsync<SampleRecord[]>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.SeriesRead, request), cancellationToken);
        await Assert.That(expected).HasSingleItem();
        await Assert.That(expected[0].Sample.EventId).IsEqualTo(RelationalSqlRf3Tokens.SampleId);
        await Assert.That(expected[0].Sample.Value).IsEqualTo(RelationalSqlRf3Tokens.SampleValue);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, official);
    }
}
