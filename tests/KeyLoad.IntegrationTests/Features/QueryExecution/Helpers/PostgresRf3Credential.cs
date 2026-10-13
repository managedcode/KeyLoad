using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Credential
{
    internal static async Task RunAsync(KeyLoadClient root, RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, PostgresRf3State state, CancellationToken token)
    {
        var original = state.Reader.Credential;
        await McpCallerAssertions.SdkStatusAsync(
            await root.ConfigureApiKeyAsync(Guid.NewGuid(), original with { Revoked = true }, token));
        var before = await PostgresRf3Assertions.ImageAsync(root, token);
        await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: false, token),
            PostgresRf3Protocol.UnauthenticatedSqlState, ErrorCode.Unauthenticated);
        var sdk = await reader.Sdk.ExecuteSqlAsync(state.Operation, token);
        await global::TUnit.Assertions.Assert.That(sdk.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var denied = await global::TUnit.Assertions.Assert.ThrowsAsync<HttpRequestException>(() =>
            reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, state.Operation, token))
            ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        await global::TUnit.Assertions.Assert.That(denied.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        await global::TUnit.Assertions.Assert.That(denied.Message.Contains(state.Reader.Secret, StringComparison.Ordinal)).IsFalse();
        await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
        await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), original, token));
        before = await PostgresRf3Assertions.ImageAsync(root, token);
        await PostgresRf3Assertions.PageAsync(state,
            await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
        await PostgresRf3Assertions.PageAsync(state,
            await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, state.Operation, token));
        await PostgresRf3Assertions.PageAsync(state,
            await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, state.Operation, token));
        await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
    }
}
