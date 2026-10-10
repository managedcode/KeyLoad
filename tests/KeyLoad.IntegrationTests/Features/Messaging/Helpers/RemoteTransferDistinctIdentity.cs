using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctIdentity
{
    internal static async Task<MessagingRf3Identity> CreateAsync(KeyLoadClient administrator,
        PrincipalRecord principal, CancellationToken token)
    {
        var key = RemoteTransferDistinctProtocol.CredentialPrefix + Guid.NewGuid().ToString(RemoteTransferColdProtocol.TransferIdFormat);
        var secret = key + RemoteTransferDistinctProtocol.Separator
            + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RemoteTransferDistinctProtocol.SecretBytes));
        var credential = new ApiKeyRecord(key, principal.Id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, token));
        await Assert.That(actual.Id).IsEqualTo(principal.Id);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), credential, token))).IsTrue();
        return new(actual, credential, secret);
    }
}
