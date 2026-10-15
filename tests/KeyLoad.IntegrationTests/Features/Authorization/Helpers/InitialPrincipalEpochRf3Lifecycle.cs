using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochRf3Lifecycle
{
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        InitialPrincipalEpochRf3State state, CancellationToken token)
    {
        await InitialPrincipalEpochRf3Setup.PrincipalAsync(root, state, 1L, true, false, token);
        await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L, async () =>
        {
            var conflict = await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Principal(0L), token);
            await Assert.That(conflict.IsFailed).IsTrue();
            await Assert.That(conflict.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
                "A policy update must advance its epoch."), conflict.Problem);
        }, token);
        await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L,
            () => McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, state.OriginalSecret, token), token);
        var originalKey = await InitialPrincipalEpochRf3Setup.KeyAsync(root, state, false, false, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.OriginalSecret, async reader =>
        {
            await InitialPrincipalEpochRf3Values.DocumentAsync(reader, state, 1L, token);
            await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L,
                () => InitialPrincipalEpochRf3Refusals.AdministrationAsync(reader, state, token), token);
            await InitialPrincipalEpochRf3Setup.PrincipalAsync(root, state, 2L, false, false, token);
            await ObserveRefusalAsync(root, reader, state, false, token);
            await InitialPrincipalEpochRf3Setup.PrincipalAsync(root, state, 3L, true, true, token);
            await ObserveRefusalAsync(root, reader, state, true, token);
            await InitialPrincipalEpochRf3Setup.PrincipalAsync(root, state, 4L, true, false, token);
            await InitialPrincipalEpochRf3Values.DocumentAsync(reader, state, 1L, token);
            await KeyRevocationAsync(root, reader, state, token);
            await InitialPrincipalEpochRf3Setup.KeyAsync(root, state, false, true, token);
            await ObserveRefusalAsync(root, reader, state, true, token);
            await InitialPrincipalEpochRf3Setup.KeyAsync(root, state, true, false, token);
            await ObserveRefusalAsync(root, reader, state, true, token);
            await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L, async () =>
            {
                await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(originalKey, state.Credential(), token));
                var args = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(state.Credential()));
                args.Add(McpCallerProtocol.CommandId, originalKey);
                await McpCallerAssertions.StatusAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(
                    McpCallerTools.CredentialsConfigure, args, token));
                await InitialPrincipalEpochRf3Refusals.ReaderAsync(reader, state, true, token);
            }, token);
            return true;
        }, token);
    }
    private static Task ObserveRefusalAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        InitialPrincipalEpochRf3State state, bool unauthenticated, CancellationToken token)
        => InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L,
            () => InitialPrincipalEpochRf3Refusals.ReaderAsync(reader, state, unauthenticated, token), token);
    private static async Task KeyRevocationAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        InitialPrincipalEpochRf3State state, CancellationToken token)
    {
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            state.Credential() with
            {
                Revoked = true
            }, token));
        await ObserveRefusalAsync(root, reader, state, true, token);
        await InitialPrincipalEpochRf3Setup.KeyAsync(root, state, false, false, token);
        await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L,
            () => InitialPrincipalEpochRf3Values.DocumentAsync(reader, state, 1L, token), token);
    }

}
