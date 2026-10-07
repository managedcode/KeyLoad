using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class ConfiguredVectorProfileRf3Flow
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
        { VectorProfiles = [new(Field, Space)] };
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(sdk, SqlRf3Protocol.Call(partition,
            McpCallerTools.ResourcesConfigure, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition),
            Guid.NewGuid()), token);
        await Assert.That(NativeSerialization.Serialize(configured).SequenceEqual(NativeSerialization.Serialize(definition))).IsTrue();
        var seed = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, Target, OriginalJson),
            new PutDocument(Collection, Rival, OriginalJson), new PutVector(Collection, Target, Field, [1, 0], Space, InitialRevision),
            new PutVector(Collection, Rival, Field, [0.5f, 0.5f], Space, InitialRevision)]);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed, token));
        await VerifyAsync(sdk, mcp, partition, changed: false, token);
        var wrong = Command(partition, Space with { Model = WrongModel });
        await RejectAsync(sdk, mcp, path, wrong, token);
        await RejectAsync(sdk, mcp, path, wrong, token);
        await VerifyAsync(sdk, mcp, partition, changed: false, token);
        var healthy = Command(partition, Space);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(healthy, token));
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.DocumentsCommit, healthy, token));
        await Assert.That(NativeSerialization.Serialize(replay.Value).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await VerifyAsync(sdk, mcp, partition, changed: true, token);
    }
    private static CommandRequest Command(PartitionRef partition, VectorSpace space)
        => new(Guid.NewGuid(), partition, [new PutDocument(Collection, Target, UpdatedJson, InitialRevision),
            new PutVector(Collection, Target, Field, [0, 1], space, UpdatedRevision)]);
    private static async Task RejectAsync(KeyLoadClient sdk, McpOfficialClient mcp, string path, CommandRequest command,
        CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
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
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Mismatch);
            return;
        }
        if (path == McpSql)
        {
            var reply = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Mismatch);
            return;
        }
        throw new InvalidOperationException("The configured-profile public path is undefined.");
    }
    private static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, PartitionRef partition, bool changed,
        CancellationToken token)
    {
        var request = new SearchRequest(partition, Collection, VectorField: Field, Vector: [1, 0], Space: Space);
        var rows = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(McpCallerTools.SearchExecute, request, token));
        await Assert.That(NativeSerialization.Serialize(rows).SequenceEqual(NativeSerialization.Serialize(official.Value))).IsTrue();
        await Assert.That(rows.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            changed ? new[] { Rival, Target } : new[] { Target, Rival }, CollectionOrdering.Matching);
        await Assert.That(rows.Select(row => row.Score)).IsEquivalentTo(new[] { FirstRank, SecondRank }, CollectionOrdering.Matching);
        foreach (var row in rows)
        {
            var updated = changed && row.Document.Reference.Id == Target;
            var expected = new DocumentResult(new(partition, Collection, row.Document.Reference.Id),
                updated ? UpdatedRevision : InitialRevision, updated ? UpdatedJson : OriginalJson, false, []);
            await Assert.That(NativeSerialization.Serialize(row.Document).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        }
    }
}
