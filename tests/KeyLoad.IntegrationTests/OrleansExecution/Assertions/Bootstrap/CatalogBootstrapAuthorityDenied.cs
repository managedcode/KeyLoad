using System.Net;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal static class CatalogBootstrapAuthorityDenied
{
    internal static async Task RootAsync(RequestCqrsClusterCallers original, CatalogBootstrapAuthorityState state,
        string variant, CancellationToken token)
    {
        var reference = new EntityRef(state.Partition, P.Collection, P.Document);
        var result = await original.Sdk.GetAsync(reference, token);
        var code = variant == P.PrincipalDemoted ? ErrorCode.PermissionDenied : ErrorCode.Unauthenticated;
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, variant == P.PrincipalDemoted ? P.ScopeDenied : P.Unavailable), result.Problem);
        if (variant == P.PrincipalDemoted)
        {
            var reply = await original.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token);
            await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
            await SqlClusterProtocol.EqualAsync(JsonSerializer.SerializeToElement(Errors.Problem(code, P.ScopeDenied), JsonDefaults.Options), reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => original.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
            await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    internal static async Task ColdRootAsync(ClusterFixture fixture, CatalogBootstrapAuthorityState state,
        string variant, CancellationToken token)
    {
        if (variant == P.PrincipalDemoted)
        {
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, P.Nodes[P.FirstIndex], fixture.AdminKey, async root =>
            {
                await RootAsync(root, state, variant, token);
                return true;
            }, token);
            return;
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, P.Nodes[P.FirstIndex]);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = new global::KeyLoad.Client.KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var result = await sdk.GetAsync(new(state.Partition, P.Collection, P.Document), token);
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value).IsNull();
                await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Unauthenticated, P.Unavailable), result.Problem);
            }, failures);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await using var attempted = await McpOfficialClient.ConnectAsync(fixture, P.Nodes[P.FirstIndex], fixture.AdminKey, token);
            });
            await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReaderAdministrationAsync(RequestCqrsClusterCallers reader,
        CatalogBootstrapAuthorityState state, CancellationToken token)
    {
        var target = state.Administrator with
        {
            PolicyEpoch = P.ChangedEpoch
        };
        var result = await reader.Sdk.ConfigurePrincipalAsync(state.ReaderDeniedCommandId, target, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, P.AdministrationDenied), result.Problem);
        var args = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(target));
        args.Add(McpCallerProtocol.CommandId, state.ReaderDeniedCommandId);
        var reply = await reader.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, args, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        await SqlClusterProtocol.EqualAsync(JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.PermissionDenied, P.McpDenied), JsonDefaults.Options), reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
    }
}
