using System.Security.Cryptography;
using Aspire.Hosting;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

/// <summary>Qualifies credential revocation independently while its genuine persisted principal stays active.</summary>
internal static class NodeEpochClusterCredentialRevocationOracle
{
    internal static async Task VerifyAsync(DistributedApplication app, NodeEpochClusterCallers admin,
        NodeEpochClusterWorkload workload, CancellationToken cancellationToken)
    {
        var keyId = "epoch-key-revocation-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var credential = DatabaseEngine.Credential(keyId, workload.Reader.PrincipalId, secret);
        await McpCallerAssertions.SdkStatusAsync(await admin.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), credential,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var independent = workload with
        {
            Reader = new(workload.Reader.PrincipalId, secret, workload.Reader.Principal, credential)
        };
        await using var callers = await NodeEpochClusterCallers.ConnectAsync(app, NodeEpochClusterProtocol.Node1,
            NodeEpochClusterProtocol.Node2, secret, cancellationToken).ConfigureAwait(false);
        await NodeEpochClusterReadOracle.VerifyDocumentAsync(callers, independent, expectedCurrent: true, cancellationToken)
            .ConfigureAwait(false);
        await McpCallerAssertions.SdkStatusAsync(await admin.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            credential with
            {
                Revoked = true
            }, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await NodeEpochClusterAuthorizationOracle.AssertDeniedAsync(callers, independent, cancellationToken)
            .ConfigureAwait(false);
    }
}
