using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed record ProtectedDocumentRf3Seed(PartitionRef Partition, EntityRef Reference,
    string ReaderId, string ReaderSecret, string DeniedSecret, CommitReceipt Original)
{
    internal ProtectedDocumentRf3MovementState Move
    {
        get; init;
    } = null!;
    internal const string Collection = "protected-documents";
    internal const string Entity = "one";
    internal const string Initial = "{\"title\":\"Київ original\",\"secret\":\"protected-private-canary\"}";

    internal static async Task<ProtectedDocumentRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, CancellationToken token)
    {
        var partition = new PartitionRef("protected-tenant", "protected-database", "protected-domain",
            "protected-" + Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies = [new("/secret", "private")]
        };
        _ = await McpCallerAssertions.SdkSuccessAsync(await source.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var reader = await PersistAsync(source, partition, Capability.DocumentsRead | Capability.Query, ["title"], token).ConfigureAwait(false);
        var denied = await PersistAsync(source, partition, Capability.None, [], token).ConfigureAwait(false);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, Entity, Initial, ExpectedRevision: 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(command, token));
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token).ConfigureAwait(false);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await source.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        var moved = await ProtectedDocumentRf3MovementFlow.RunAsync(wave, source, command, receipt,
            directory, placement, denied.Secret, token).ConfigureAwait(false);
        return new(partition, new(partition, Collection, Entity), reader.Id, reader.Secret, denied.Secret, receipt)
        {
            Move = moved
        };
    }

    private static async Task<(string Id, string Secret)> PersistAsync(KeyLoadClient source,
        PartitionRef partition, Capability capability, string[] fields, CancellationToken token)
    {
        var id = "c1-probe-protected-" + Guid.NewGuid().ToString("N");
        var key = "protected-key-" + Guid.NewGuid().ToString("N");
        var secret = key + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var principal = new PrincipalRecord(id, partition.TenantId,
            capability == Capability.None ? [] : [new(partition.DatabaseId, Collection, capability)], [.. fields]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await source.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        _ = await McpCallerAssertions.SdkStatusAsync(await source.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(key, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))), token));
        return (id, secret);
    }
}
