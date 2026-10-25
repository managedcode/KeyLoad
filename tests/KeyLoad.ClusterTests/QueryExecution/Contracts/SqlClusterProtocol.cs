using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.QueryExecution;

/// <summary>Canonical SQL envelopes used by real SDK and official MCP callers.</summary>
internal static class SqlClusterProtocol
{
    internal const string SyntaxProfile = "TSQL.ScriptDOM180";
    internal const string ReadUnavailable = "The read response is unavailable.";
    internal const string Argument = "args";
    internal const string CallPrefix = "EXEC ";
    internal const string CallSuffix = " @args";
    internal const string TableSelect = "SELECT * FROM agentrows WHERE 'alpha' = title ORDER BY id";
    internal const string IndexPath = "index:by-title";

    internal static SqlOperationRequest Call<T>(PartitionRef partition, string operation, T request,
        Guid? outerCommandId = null)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
        };
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

    /// <summary>The SDK reports a caller-cancelled read as a failed result with no value; it does not throw.</summary>
    internal static async Task SdkCancelledAsync<T>(Result<T> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await EqualAsync(Errors.Problem(ErrorCode.Cancelled, ReadUnavailable), result.Problem);
    }

    private static SqlOperationRequest Envelope(PartitionRef partition, string operation, Dictionary<string, JsonElement> arguments)
        => new(partition, CallPrefix + operation + CallSuffix,
            new()
            {
                [Argument] = JsonSerializer.SerializeToElement(arguments, JsonDefaults.Options)
            });
}
