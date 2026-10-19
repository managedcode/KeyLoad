using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochClusterLifecycle
{
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        InitialPrincipalEpochClusterState state, CancellationToken token)
    {
        await InitialPrincipalEpochClusterSetup.PrincipalAsync(root, state, 1L, true, false, token);
        await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L, async () =>
        {
            var conflict = await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Principal(0L), token);
            await Assert.That(conflict.IsFailed).IsTrue();
            await Assert.That(conflict.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
                "A policy update must advance its epoch."), conflict.Problem);
        }, token);
        await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L,
            () => McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, state.OriginalSecret, token), token);
        var originalKey = await InitialPrincipalEpochClusterSetup.KeyAsync(root, state, false, false, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.OriginalSecret, async reader =>
        {
            await InitialPrincipalEpochClusterValues.DocumentAsync(reader, state, 1L, token);
            await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L,
                () => InitialPrincipalEpochClusterRefusals.AdministrationAsync(reader, state, token), token);
            await InitialPrincipalEpochClusterSetup.PrincipalAsync(root, state, 2L, false, false, token);
            await ObserveRefusalAsync(root, reader, state, false, token);
            await InitialPrincipalEpochClusterSetup.PrincipalAsync(root, state, 3L, true, true, token);
            await ObserveRefusalAsync(root, reader, state, true, token);
            await InitialPrincipalEpochClusterSetup.PrincipalAsync(root, state, 4L, true, false, token);
            await InitialPrincipalEpochClusterValues.DocumentAsync(reader, state, 1L, token);
            await KeyRevocationAsync(root, reader, state, token);
            await InitialPrincipalEpochClusterSetup.KeyAsync(root, state, false, true, token);
            await ObserveRefusalAsync(root, reader, state, true, token);
            await InitialPrincipalEpochClusterSetup.KeyAsync(root, state, true, false, token);
            await ObserveRefusalAsync(root, reader, state, true, token);
            await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L, async () =>
            {
                await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(originalKey, state.Credential(), token));
                var args = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(state.Credential()));
                args.Add(McpCallerProtocol.CommandId, originalKey);
                await McpCallerAssertions.StatusAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(
                    McpCallerTools.CredentialsConfigure, args, token));
                await InitialPrincipalEpochClusterRefusals.ReaderAsync(reader, state, true, token);
            }, token);
            return true;
        }, token);
    }
    private static Task ObserveRefusalAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        InitialPrincipalEpochClusterState state, bool unauthenticated, CancellationToken token)
        => InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L,
            () => InitialPrincipalEpochClusterRefusals.ReaderAsync(reader, state, unauthenticated, token), token);
    private static async Task KeyRevocationAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        InitialPrincipalEpochClusterState state, CancellationToken token)
    {
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            state.Credential() with
            {
                Revoked = true
            }, token));
        await ObserveRefusalAsync(root, reader, state, true, token);
        await InitialPrincipalEpochClusterSetup.KeyAsync(root, state, false, false, token);
        await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L,
            () => InitialPrincipalEpochClusterValues.DocumentAsync(reader, state, 1L, token), token);
    }

}
