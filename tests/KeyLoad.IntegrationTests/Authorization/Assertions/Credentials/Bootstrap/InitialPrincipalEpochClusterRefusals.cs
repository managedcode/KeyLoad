using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochClusterRefusals
{
    internal static async Task InitialAsync(RequestCqrsClusterCallers root, InitialPrincipalEpochClusterState state,
        long revision, CancellationToken token)
        => await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, revision, async () =>
        {
            var sdk = await root.Sdk.ConfigurePrincipalAsync(state.RefusedId, state.Refused, token);
            await Assert.That(sdk.IsFailed).IsTrue();
            await Assert.That(sdk.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Validation, InitialPrincipalEpochClusterState.InvalidDetail), sdk.Problem);
            var args = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(state.Refused));
            args.Add(McpCallerProtocol.CommandId, state.RefusedId);
            var reply = await root.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, args, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
            await ProblemAsync(reply, ErrorCode.Validation, "The database operation could not be completed.");
        }, token);
    internal static async Task MissingAsync(RequestCqrsClusterCallers root, InitialPrincipalEpochClusterState state, CancellationToken token)
    {
        var result = await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), state.Credential(), token);
        await Assert.That(result.IsFailed).IsTrue();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Unauthenticated, InitialPrincipalEpochClusterState.Unavailable), result.Problem);
    }
    internal static async Task ReaderAsync(RequestCqrsClusterCallers reader, InitialPrincipalEpochClusterState state,
        bool unauthenticated, CancellationToken token)
    {
        var code = unauthenticated ? ErrorCode.Unauthenticated : ErrorCode.PermissionDenied;
        var sdk = await reader.Sdk.GetAsync(state.Reference, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, unauthenticated ? InitialPrincipalEpochClusterState.Unavailable
            : "The principal cannot perform this operation in this scope."), sdk.Problem);
        var encoded = System.Text.Json.JsonSerializer.Serialize(sdk, JsonDefaults.Options);
        await PrivateAsync(encoded, state);
        if (unauthenticated)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => reader.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Reference), token));
            await Assert.That(error!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
            await PrivateAsync(error.Message, state);
        }
        else
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Reference), token);
            await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
            await ProblemAsync(reply, code, "The principal cannot perform this operation in this scope.");
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.OriginalSecret, state.ReplacementSecret);
        }
    }
    private static async Task PrivateAsync(string text, InitialPrincipalEpochClusterState state)
    {
        await Assert.That(text.Contains(state.OriginalSecret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(state.ReplacementSecret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("{\"value\":7}", StringComparison.Ordinal)).IsFalse();
    }
    internal static async Task AdministrationAsync(RequestCqrsClusterCallers reader, InitialPrincipalEpochClusterState state, CancellationToken token)
    {
        var forged = state.Principal(2L) with
        {
            ClusterAdministrator = true,
            TenantId = "forged-tenant"
        };
        var result = await reader.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), forged, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, "Cluster administration is required."), result.Problem);
        var args = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(forged));
        args.Add(McpCallerProtocol.CommandId, Guid.NewGuid());
        var reply = await reader.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, args, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        await ProblemAsync(reply, ErrorCode.PermissionDenied, "The authenticated principal is not permitted to perform this operation.");
        await InitialPrincipalEpochClusterValues.DocumentAsync(reader, state, 1L, token);
    }
    private static async Task ProblemAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options);
        var actual = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        await Assert.That(JsonElement.DeepEquals(expected, actual)).IsTrue();
    }
}
