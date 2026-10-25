using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Search;

internal static class ConfiguredVectorProfileClusterFlow
{
    internal const string Sdk = "sdk";
    internal const string Mcp = "mcp";
    internal const string SdkSql = "sdk-sql";
    internal const string McpSql = "mcp-sql";
    private const string Tenant = "vector-tenant";
    private const string DatabasePrefix = "vector-";
    private const string Domain = "vector-domain";
    private const string Bucket = "vector-bucket";
    private const string Collection = "configured-vectors";
    private const string Target = "target";
    private const string Rival = "rival";
    private const string Field = "/embedding";
    private const string OriginalJson = """{"state":"original"}""";
    private const string UpdatedJson = """{"state":"updated"}""";
    private const int Dimension = 2;
    private const string SpaceId = "configured-space";
    private const string ModelId = "trusted-model";
    private const string ModelVersion = "1";
    private const string WrongModel = "other-model";
    private const string Mismatch = "The declared vector profile does not match the configured field.";
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;
    private const double FirstRank = 1d / 61;
    private const double SecondRank = 1d / 62;
    private static readonly VectorSpace Space = new(SpaceId, Dimension, DistanceMetric.DotProduct, ModelId, ModelVersion);

    internal static async Task RunAsync(ClusterFixture fixture, string path, CancellationToken token)
    {
        var partition = new PartitionRef(Tenant, DatabasePrefix + Guid.NewGuid().ToString("N"), Domain, Bucket);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, token);
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            VectorProfiles = [new(Field, Space)]
        };
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var configured = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(sdk, SqlClusterProtocol.Call(partition,
            McpCallerTools.ResourcesConfigure, creation, creationId), token);
        var lifetime = ResourceCreationLifetime.Create(creationId, creation);
        await Assert.That(OrleansSerialization.Serialize(configured).SequenceEqual(OrleansSerialization.Serialize(definition))).IsTrue();
        var seed = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, Target, OriginalJson),
            new PutDocument(Collection, Rival, OriginalJson), new PutVector(Collection, Target, Field, [1, 0], Space, InitialRevision),
            new PutVector(Collection, Rival, Field, [0.5f, 0.5f], Space, InitialRevision)]);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed, token));
        await VerifyAsync(sdk, mcp, partition, lifetime, changed: false, token);
        var wrong = Command(partition, Space with
        {
            Model = WrongModel
        });
        await RejectAsync(sdk, mcp, path, wrong, token);
        await RejectAsync(sdk, mcp, path, wrong, token);
        await VerifyAsync(sdk, mcp, partition, lifetime, changed: false, token);
        var healthy = Command(partition, Space);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(healthy, token));
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.DocumentsCommit, healthy, token));
        await Assert.That(OrleansSerialization.Serialize(replay.Value).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await VerifyAsync(sdk, mcp, partition, lifetime, changed: true, token);
    }
    private static CommandRequest Command(PartitionRef partition, VectorSpace space)
        => new(Guid.NewGuid(), partition, [new PutDocument(Collection, Target, UpdatedJson, InitialRevision),
            new PutVector(Collection, Target, Field, [0, 1], space, UpdatedRevision)]);
    private static async Task RejectAsync(KeyLoadClient sdk, McpOfficialClient mcp, string path, CommandRequest command,
        CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        if (path == Sdk)
        {
            var failure = await sdk.CommitAsync(command, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Value).IsNull();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.Validation.ToString());
            await Assert.That(failure.Problem?.Detail).IsEqualTo(Mismatch);
            return;
        }
        if (path == SdkSql)
        {
            var failure = await sdk.ExecuteSqlAsync(sql, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.Validation.ToString());
            await Assert.That(failure.Problem?.Detail).IsEqualTo(Mismatch);
            return;
        }
        if (path == Mcp)
        {
            var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
            _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true)).Value;
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Mismatch);
            return;
        }
        if (path == McpSql)
        {
            var reply = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
            _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true)).Value;
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Mismatch);
            return;
        }
        throw new InvalidOperationException("The configured-profile public path is undefined.");
    }
    private static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, PartitionRef partition,
        EntityRefLifetime lifetime, bool changed, CancellationToken token)
    {
        var request = new SearchRequest(partition, Collection, VectorField: Field, Vector: [1, 0], Space: Space);
        var rows = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(McpCallerTools.SearchExecute, request, token));
        await Assert.That(OrleansSerialization.Serialize(rows).SequenceEqual(OrleansSerialization.Serialize(official.Value))).IsTrue();
        await Assert.That(rows.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            changed ? new[] { Rival, Target } : new[] { Target, Rival }, CollectionOrdering.Matching);
        await Assert.That(rows.Select(row => row.Score)).IsEquivalentTo(new[] { FirstRank, SecondRank }, CollectionOrdering.Matching);
        foreach (var row in rows)
        {
            var updated = changed && row.Document.Reference.Id == Target;
            var expected = new DocumentResult(new(partition, Collection, row.Document.Reference.Id) { Lifetime = lifetime },
                updated ? UpdatedRevision : InitialRevision, updated ? UpdatedJson : OriginalJson, false, []);
            await Assert.That(OrleansSerialization.Serialize(row.Document).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
        }
    }
}
