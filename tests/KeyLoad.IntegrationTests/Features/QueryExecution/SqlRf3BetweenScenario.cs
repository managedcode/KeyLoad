using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Independent actual RF3 corpus and literal row oracle for AC-SQLC-006A.</summary>
internal sealed record SqlRf3BetweenScenario(PartitionRef Partition, long CommittedPosition)
{
    internal const string Lower = "lower";
    internal const string Middle = "middle";
    internal const string Upper = "upper";
    internal const string Below = "below";
    internal const string Above = "above";
    internal const string Null = "null";
    internal const string Missing = "missing";
    internal const string IndexPath = "index:by-group";
    internal const string Positive = "score BETWEEN @lo AND @hi";
    internal const string Negative = "score NOT BETWEEN 2 AND 8";
    internal const string LowerUnknownNegative = "score NOT BETWEEN NULL AND 2";
    internal const string UpperUnknownNegative = "score NOT BETWEEN 2 AND NULL";
    internal const string LowerUnknown = "score BETWEEN NULL AND 8";
    internal const string UpperUnknown = "score BETWEEN 2 AND NULL";
    internal const string Malformed = "score BETWEEN 2 OR 8";
    internal const string QuotedOperator = "score \"BETWEEN\" 2 AND 8";
    internal const string EagerMismatch = "score BETWEEN 20 AND 'different-type'";
    internal const string MissingParameter = "score BETWEEN @absent AND 8";
    internal const string NonScalarParameter = "score BETWEEN @lo AND @hi";
    private const string Collection = "betweenrows";
    private const string TenantPrefix = "sql-between-";
    private const string DatabaseId = "database";
    private const string Domain = "range";
    private const string GroupIndex = "by-group";
    private const string GroupPath = "/groupkey";
    private const string LowerParameter = "lo";
    private const string UpperParameter = "hi";
    private const string SqlPrefix = "SELECT id FROM betweenrows WHERE groupkey = 'shared' AND ";
    private const string SqlSuffix = " ORDER BY id";
    private const decimal Minimum = 2m;
    private const decimal Maximum = 8m;

    internal static async Task<SqlRf3BetweenScenario> CreateAsync(KeyLoadClient administrator,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            DatabaseId, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        { Indexes = [new(GroupIndex, [GroupPath])] };
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(administrator,
            SqlRf3Protocol.Call(partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), Guid.NewGuid()),
            cancellationToken);
        await Assert.That(configured.Indexes[0].Name).IsEqualTo(GroupIndex);
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, Below, "{\"groupkey\":\"shared\",\"score\":1}"),
            new PutDocument(Collection, Lower, "{\"groupkey\":\"shared\",\"score\":2}"),
            new PutDocument(Collection, Middle, "{\"groupkey\":\"shared\",\"score\":5}"),
            new PutDocument(Collection, Upper, "{\"groupkey\":\"shared\",\"score\":8}"),
            new PutDocument(Collection, Above, "{\"groupkey\":\"shared\",\"score\":9}"),
            new PutDocument(Collection, Null, "{\"groupkey\":\"shared\",\"score\":null}"),
            new PutDocument(Collection, Missing, "{\"groupkey\":\"shared\"}")
        ]);
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(administrator,
            SqlRf3Protocol.Call(partition, McpCallerTools.DocumentsCommit, command), cancellationToken);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(command.Mutations.Length);
        return new(partition, receipt.Token.Position);
    }

    internal SqlOperationRequest Request(string predicate, Dictionary<string, JsonElement>? parameters = null)
        => new(Partition, SqlPrefix + predicate + SqlSuffix, parameters);

    internal static Dictionary<string, JsonElement> Parameters(bool nonScalar = false) => new(StringComparer.Ordinal)
    {
        [LowerParameter] = nonScalar ? JsonSerializer.SerializeToElement(new[] { Minimum }, JsonDefaults.Options)
            : JsonSerializer.SerializeToElement(Minimum, JsonDefaults.Options),
        [UpperParameter] = JsonSerializer.SerializeToElement(Maximum, JsonDefaults.Options)
    };
}
