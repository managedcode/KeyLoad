using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class CredentialVerifierR53Flow
{
    internal static async Task WarmAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient oldReader,
        McpOfficialClient oldSession, CredentialSubjectReplacementState state, CancellationToken token)
    {
        var verifier = new CredentialVerifierR53State(state);
        verifier.Replacement = AuthorizationExecution.Credential(state.First.Identity.Credential.Id,
            state.First.Identity.Principal.Id, verifier.Secret, state.First.Identity.Credential.ExpiresAt);
        await Assert.That(verifier.Replacement.Id).IsEqualTo(state.First.Identity.Credential.Id);
        await Assert.That(verifier.Replacement.PrincipalId).IsEqualTo(state.First.Identity.Credential.PrincipalId);
        await Assert.That(verifier.Replacement.ExpiresAt).IsEqualTo(state.First.Identity.Credential.ExpiresAt);
        await Assert.That(verifier.Replacement.Revoked).IsEqualTo(state.First.Identity.Credential.Revoked);
        await Assert.That(string.Equals(verifier.Secret, state.First.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(verifier.Replacement.Verifier, state.First.Identity.Credential.Verifier, StringComparison.Ordinal)).IsFalse();
        state.Verifier = verifier;
        await CredentialVerifierR53Administrator.RunAsync(fixture, async administrator =>
        {
            await CredentialVerifierR53Current.CaptureAsync(root, administrator, state, verifier, token);
            verifier.ExpireResult = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(
                verifier.ExpireId, verifier.Expired, token));
            await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
            await CredentialVerifierR53Refusals.SdkAsync(oldReader, state, verifier, token);
            await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
            await CredentialVerifierR53Refusals.LiveAsync(oldSession, state, verifier, token);
            await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
            verifier.ReplaceResult = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(
                verifier.ReplaceId, verifier.Replacement, token));
            await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
            await CredentialVerifierR53Refusals.SdkAsync(oldReader, state, verifier, token);
            await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
            await CredentialVerifierR53Refusals.LiveAsync(oldSession, state, verifier, token);
            await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
            state.CurrentSecret = verifier.Secret;
            verifier.Active = true;
            await CredentialSubjectReplacementClients.RunAsync(fixture, state, warm: false, token);
        }, token);
    }

    internal static async Task ContextAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, CredentialSubjectReplacementState state, CancellationToken token)
    {
        var verifier = state.Verifier ?? throw new InvalidOperationException(CredentialVerifierR53Protocol.MissingState);
        await CredentialVerifierR53Administrator.RunAsync(fixture, async administrator =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var oldHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var oldReader = new KeyLoadClient(oldHttp, state.First.Identity.Secret, IntegrationClientOptions.Execution());
                    async Task CurrentAsync()
                    {
                        await CredentialVerifierR53Current.ReaderAsync(root, reader, session, administrator, state, verifier, token);
                        await CredentialVerifierR53Refusals.SdkAsync(oldReader, state, verifier, token);
                        await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
                    }
                    await CurrentAsync();
                    await CredentialVerifierR53Refusals.InitializeAsync(fixture, state, verifier, token);
                    await CredentialVerifierR53Current.RequireAsync(root, administrator, state, verifier, token);
                    await CredentialVerifierR53Replay.RunAsync(root, administrator, state, verifier, CurrentAsync, token);
                    await CurrentAsync();
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, token);
    }
}
