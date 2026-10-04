using System.Security.Cryptography;
using Aspire.Hosting;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

/// <summary>Qualifies credential revocation independently while its genuine persisted principal stays active.</summary>
internal static class NodeEpochRf3CredentialRevocationOracle
{
    internal static async Task VerifyAsync(DistributedApplication app, NodeEpochRf3Callers admin,
        NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        var keyId = "epoch-key-revocation-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var credential = DatabaseEngine.Credential(keyId, workload.Reader.PrincipalId, secret);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), credential,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var independent = workload with
        { Reader = new(workload.Reader.PrincipalId, secret, workload.Reader.Principal, credential) };
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app, NodeEpochRf3Protocol.Node1,
            NodeEpochRf3Protocol.Node2, secret, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifyDocumentAsync(callers, independent, expectedCurrent: true, cancellationToken)
            .ConfigureAwait(false);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            credential with { Revoked = true }, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await NodeEpochRf3AuthorizationOracle.AssertDeniedAsync(callers, independent, cancellationToken)
            .ConfigureAwait(false);
    }
}
