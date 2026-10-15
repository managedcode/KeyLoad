using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Seed
{
    internal static async Task<SafeProjectorOperationRf3State> CreateAsync(RequestCqrsRf3Callers root,
        PartitionRef partition, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(SafeProjectorOperationRf3Protocol.GuidFormat);
        var collection = SafeProjectorOperationRf3Protocol.Collection + suffix;
        var reader = SafeProjectorOperationRf3Protocol.Reader + suffix;
        var key = reader + SafeProjectorOperationRf3Protocol.CredentialSuffix;
        var resource = new ResourceDefinition(collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Indexes = [new(SafeProjectorOperationRf3Protocol.Index, [SafeProjectorOperationRf3Protocol.StatusPath])],
            FieldPolicies =
            [
                Policy(SafeProjectorOperationRf3Protocol.NestedPath),
                Policy(SafeProjectorOperationRf3Protocol.LiteralPath),
                Policy(SafeProjectorOperationRf3Protocol.ArrayPath)
            ]
        };
        var resourceId = Guid.NewGuid();
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(resourceId, new(partition.TenantId, partition.DatabaseId, resource), token)));
        var principal = new PrincipalRecord(reader, partition.TenantId,
            [new(partition.DatabaseId, collection,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query | Capability.ChangesRead)], [])
        {
            PolicyEpoch = SafeProjectorOperationRf3Protocol.InitialPolicyEpoch
        };
        var principalId = Guid.NewGuid();
        await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(principalId, principal, token)));
        var credentialId = Guid.NewGuid();
        var credential = DatabaseEngine.Credential(reader, reader, key);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(credentialId, credential, token));
        var seed = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(collection, SafeProjectorOperationRf3Protocol.First, SafeProjectorOperationRf3Protocol.InitialJson,
                ExpectedRevision: SafeProjectorOperationRf3Protocol.AbsentRevision),
            new PutDocument(collection, SafeProjectorOperationRf3Protocol.Second, SafeProjectorOperationRf3Protocol.SecondJson,
                ExpectedRevision: SafeProjectorOperationRf3Protocol.AbsentRevision)
        ]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(SafeProjectorOperationRf3Protocol.PlacementReadVersion, partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await SafeProjectorOperationRf3Receipts.TokenAsync(receipt.Token, partition, placement);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(seed.CommandId, receipt.Token,
        [
            new(SafeProjectorOperationRf3Protocol.PutKind, collection, SafeProjectorOperationRf3Protocol.First,
                SafeProjectorOperationRf3Protocol.InitialRevision),
            new(SafeProjectorOperationRf3Protocol.PutKind, collection, SafeProjectorOperationRf3Protocol.Second,
                SafeProjectorOperationRf3Protocol.InitialRevision)
        ], DurabilityProfile.QuorumProcessDurable), receipt);
        return new(partition, collection, reader, key, resource, resourceId, principalId, principal,
            credentialId, credential, seed, receipt);
    }

    private static SensitiveFieldPolicy Policy(string path) => new(path, SafeProjectorOperationRf3Protocol.Classification,
        RawReadGrant: SafeProjectorOperationRf3Protocol.ReadGrant, RawUseGrant: SafeProjectorOperationRf3Protocol.UseGrant,
        WriteGrant: SafeProjectorOperationRf3Protocol.WriteGrant);
}
