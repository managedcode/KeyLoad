using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialSubjectReplacementFlow
{
    internal static async Task WarmAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        CredentialSubjectReplacementState state, CancellationToken token)
    {
        await CredentialSubjectReplacementAssertions.StageAsync(root, reader, session, state, second: false, token);
        var before = await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token);
        state.RebindResult = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(
            state.RebindCommandId, state.Rebound, token));
        await SqlClusterProtocol.EqualAsync(before, await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        await CredentialSubjectReplacementAssertions.StageAsync(root, reader, session, state, second: true, token);
        state.RepairResult = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(
            state.RepairCommandId, state.First.Identity.Credential, token));
        await SqlClusterProtocol.EqualAsync(before, await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        await ReplayedAsync(root, reader, session, state, token);
    }

    internal static async Task ReplayedAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        CredentialSubjectReplacementState state, CancellationToken token)
    {
        await CredentialSubjectReplacementAssertions.StageAsync(root, reader, session, state, second: false, token);
        var before = await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token);
        await ReplayAsync(root, state.RebindCommandId, state.Rebound, state.RebindResult, token);
        await SqlClusterProtocol.EqualAsync(before, await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        await CredentialSubjectReplacementAssertions.StageAsync(root, reader, session, state, second: false, token);
        await ReplayAsync(root, state.RepairCommandId, state.First.Identity.Credential, state.RepairResult, token);
        await SqlClusterProtocol.EqualAsync(before, await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        await CredentialSubjectReplacementAssertions.SeedsAsync(root, state, token);
    }

    private static async Task ReplayAsync(KeyLoadClient root, Guid command, ApiKeyRecord credential,
        global::ManagedCode.Communication.Result? original, CancellationToken token)
    {
        if (original is not { IsSuccess: true, Problem: null })
        {
            throw new InvalidOperationException("The original credential configuration outcome is absent.");
        }
        var actual = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(command, credential, token));
        await SqlClusterProtocol.EqualAsync(original, actual);
    }
}
