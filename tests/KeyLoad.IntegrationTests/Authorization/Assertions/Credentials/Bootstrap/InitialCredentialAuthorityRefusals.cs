using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class InitialCredentialAuthorityRefusals
{
    internal static async Task VerifyAsync(ClusterFixture fixture, global::KeyLoad.Client.KeyLoadClient root,
        InitialCredentialAuthorityState state, string? invalidKey, CancellationToken token)
    {
        var before = await InitialCredentialAuthorityValues.ImageAsync(root, state, token);
        var admitted = false;
        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await using var unexpected = await McpOfficialClient.ConnectAsync(
                fixture, McpCallerProtocol.Node1, invalidKey, token);
            admitted = true;
        });
        await Assert.That(admitted).IsFalse();
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(McpDocumentProtocol.InitialJson, StringComparison.Ordinal)).IsFalse();
        await Assert.That(error.Message.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        if (invalidKey is not null)
        {
            await Assert.That(error.Message.Contains(invalidKey, StringComparison.Ordinal)).IsFalse();
        }
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node1, invalidKey, token);
        await MalformedCredentialAuthorityRefusals.VerifyAsync(fixture, root, state, token);
        await SqlClusterProtocol.EqualAsync(before, await InitialCredentialAuthorityValues.ImageAsync(root, state, token));
    }
}
