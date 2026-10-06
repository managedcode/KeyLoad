using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOperationTestData
{
    internal const string Parameter = "arguments";
    internal const string ExtraParameter = "extra";
    internal const string Select = "SELECT * FROM orders WHERE status = @status";
    internal const string Explain = "EXPLAIN SELECT * FROM orders";
    internal const string InvalidSelect = "SELECT malformed";
    internal const string Cursor = "signed-continuation";
    internal const string Status = "status";
    internal const string Open = "open";
    internal const string CallPrefix = "CALL ";
    internal const string CallSuffix = "(@arguments)";
    internal const string EmptyObject = "{}";
    internal const string Null = "null";
    internal const string Array = "[]";
    internal const string RecursiveCall = "CALL keyload_sql_execute(@arguments)";
    internal const string UnknownCall = "CALL keyload_missing(@arguments)";
    internal const string WrongCaseCall = "CALL KEYLOAD_QUERY_CAPABILITIES(@arguments)";
    internal const string WrongCaseKey = "Request";
    internal const string PrivateMarker = "private-input-canary";
    internal const int MaximumPayloadBytes = 65_536;
    internal static readonly DatabaseLimits Limits = new();

    internal static SqlOperationRequest Call(string name, IDictionary<string, JsonElement>? arguments = null) =>
        Request(CallPrefix + name + CallSuffix, JsonSerializer.SerializeToElement(arguments ??
            new Dictionary<string, JsonElement>(StringComparer.Ordinal), JsonDefaults.Options));

    internal static SqlOperationRequest Request(string sql, JsonElement parameter) =>
        new(McpCanonicalTestData.Partition, sql,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [Parameter] = parameter });

    internal static McpDecodedOperation Compile(SqlOperationRequest request, DatabaseLimits? limits = null,
        int maximumPayloadBytes = MaximumPayloadBytes, CancellationToken cancellationToken = default) =>
        SqlOperationCompiler.Compile(request, Microsoft.Extensions.Options.Options.Create(limits ?? Limits), maximumPayloadBytes, UnitExecutionOptions.QueryExecution(), cancellationToken);

    internal static McpOperationDescriptor Find(string name) => McpOperationCatalog.TryGet(name, out var descriptor)
        ? descriptor : throw new InvalidOperationException(name);

    internal static async Task Same(McpDecodedOperation actual, McpDecodedOperation expected)
    {
        await Assert.That(actual.ReadKind).IsEqualTo(expected.ReadKind);
        await Assert.That(actual.CommandKind).IsEqualTo(expected.CommandKind);
        await Assert.That(actual.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(actual.Payload.Span.SequenceEqual(expected.Payload.Span)).IsTrue();
    }

    internal static async Task Reject(SqlOperationRequest request, ErrorCode code,
        DatabaseLimits? limits = null, int maximumPayloadBytes = MaximumPayloadBytes)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Compile(request, limits, maximumPayloadBytes));
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).DoesNotContain(PrivateMarker);
    }
}
