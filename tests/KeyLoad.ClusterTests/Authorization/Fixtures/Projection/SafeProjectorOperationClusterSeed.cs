using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Core;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterSeed
{
    internal static async Task<SafeProjectorOperationClusterState> CreateAsync(RequestCqrsClusterCallers root,
        PartitionRef partition, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(SafeProjectorOperationClusterProtocol.GuidFormat);
        var collection = SafeProjectorOperationClusterProtocol.Collection + suffix;
        var reader = SafeProjectorOperationClusterProtocol.Reader + suffix;
        var key = reader + SafeProjectorOperationClusterProtocol.CredentialSuffix;
        var resource = new ResourceDefinition(collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Indexes = [new(SafeProjectorOperationClusterProtocol.Index, [SafeProjectorOperationClusterProtocol.StatusPath])],
            FieldPolicies =
            [
                Policy(SafeProjectorOperationClusterProtocol.NestedPath),
                Policy(SafeProjectorOperationClusterProtocol.LiteralPath),
                Policy(SafeProjectorOperationClusterProtocol.ArrayPath)
            ]
        };
        var resourceId = Guid.NewGuid();
        await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(resourceId, new(partition.TenantId, partition.DatabaseId, resource), token)));
        var principal = new PrincipalRecord(reader, partition.TenantId,
            [new(partition.DatabaseId, collection,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query | Capability.ChangesRead)], [])
        {
            PolicyEpoch = SafeProjectorOperationClusterProtocol.InitialPolicyEpoch
        };
        var principalId = Guid.NewGuid();
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(principalId, principal, token)));
        var credentialId = Guid.NewGuid();
        var credential = AuthorizationExecution.Credential(reader, reader, key);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(credentialId, credential, token));
        var seed = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(collection, SafeProjectorOperationClusterProtocol.First, SafeProjectorOperationClusterProtocol.InitialJson,
                ExpectedRevision: SafeProjectorOperationClusterProtocol.AbsentRevision),
            new PutDocument(collection, SafeProjectorOperationClusterProtocol.Second, SafeProjectorOperationClusterProtocol.SecondJson,
                ExpectedRevision: SafeProjectorOperationClusterProtocol.AbsentRevision)
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await SafeProjectorOperationClusterReceipts.TokenAsync(receipt.Token, partition);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(seed.CommandId, receipt.Token,
        [
            new(SafeProjectorOperationClusterProtocol.PutKind, collection, SafeProjectorOperationClusterProtocol.First,
                SafeProjectorOperationClusterProtocol.InitialRevision),
            new(SafeProjectorOperationClusterProtocol.PutKind, collection, SafeProjectorOperationClusterProtocol.Second,
                SafeProjectorOperationClusterProtocol.InitialRevision)
        ], DurabilityProfile.ProcessDurable, 3), receipt);
        return new(partition, collection, reader, key, resource, resourceId, principalId, principal,
            credentialId, credential, seed, receipt);
    }

    private static SensitiveFieldPolicy Policy(string path) => new(path, SafeProjectorOperationClusterProtocol.Classification,
        RawReadGrant: SafeProjectorOperationClusterProtocol.ReadGrant, RawUseGrant: SafeProjectorOperationClusterProtocol.UseGrant,
        WriteGrant: SafeProjectorOperationClusterProtocol.WriteGrant);
}
