using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Canonical SQL envelopes used by real SDK and official MCP callers.</summary>
internal static class SqlRf3Protocol
{
    internal const string Argument = "args";
    internal const string CallPrefix = "CALL ";
    internal const string CallSuffix = "(@args)";
    internal const string BlobMetadataTool = "keyload_blobs_metadata";
    internal const string BlobListTool = "keyload_blobs_list";
    internal const string TableSelect = "SELECT * FROM agentrows WHERE 'alpha' = title ORDER BY id";
    internal const string IndexPath = "index:by-title";

    internal static SqlOperationRequest Call<T>(PartitionRef partition, string operation, T request,
        Guid? outerCommandId = null)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options) };
        if (outerCommandId is { } commandId)
        {
            arguments.Add(McpCallerProtocol.CommandId, JsonSerializer.SerializeToElement(commandId, JsonDefaults.Options));
        }
        return Envelope(partition, operation, arguments);
    }

    internal static SqlOperationRequest NoBodyCall(PartitionRef partition, string operation)
        => Envelope(partition, operation, new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    internal static async Task<T> SdkAsync<T>(KeyLoadClient sdk, SqlOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(request, cancellationToken));
        return result.Deserialize<T>(JsonDefaults.Options)!;
    }

    internal static async Task<T> McpAsync<T>(McpOfficialClient session, SqlOperationRequest request, CancellationToken cancellationToken)
        => (await McpCallerAssertions.SuccessAsync<T>(await session.CallAsync(SqlOperationProtocol.ToolName,
            request, cancellationToken))).Value;

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    private static SqlOperationRequest Envelope(PartitionRef partition, string operation, Dictionary<string, JsonElement> arguments)
        => new(partition, CallPrefix + operation + CallSuffix,
            new() { [Argument] = JsonSerializer.SerializeToElement(arguments, JsonDefaults.Options) });
}
