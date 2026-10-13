using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPackingActivationRf3Provisioning
{
    private const string Prefix = "packed-native-";
    private const string SharedPartition = "same-partition-key";
    private const string SharedDomain = "same-domain";
    private const int SecretBytes = 32;
    private const long InitialPolicyEpoch = 1;

    internal static async Task<RequestCqrsPhaseFaultIdentity> CreateAsync(KeyLoadClient administrator,
        string tenant, CancellationToken token)
    {
        var principal = Prefix + Guid.NewGuid().ToString("N");
        var key = principal + "-key";
        var secret = key + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var partition = new PartitionRef(tenant, RequestCqrsRf3Protocol.Database, SharedDomain, SharedPartition);
        var definition = new PrincipalRecord(principal, tenant,
            [new(partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        { PolicyEpoch = InitialPolicyEpoch, ClusterAdministrator = false };
        var credential = new ApiKeyRecord(key, principal,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId,
                new(RequestCqrsRf3Protocol.AdminCollection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), definition, token));
        await SqlRf3Protocol.EqualAsync(definition, actual);
        await Assert.That((await McpCallerAssertions.SdkStatusAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), credential, token))).IsSuccess).IsTrue();
        return new(partition, principal, secret);
    }

    internal static CommandRequest Seed(RequestCqrsPhaseFaultIdentity identity, Guid sharedId)
        => new(sharedId, identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                RequestCqrsRf3Protocol.DocumentJson, ExpectedRevision: 0)]);

    internal static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers callers,
        CommandRequest request, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(request, token));
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt("putDocument", RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId, 1), receipt.Mutations[0]);
        await TenantPackingActivationRf3Assertions.ReplayAsync(callers, request, receipt, token);
        return receipt;
    }
}
