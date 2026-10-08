using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed record ProtectedDocumentRf3Seed(PartitionRef Partition, EntityRef Reference,
    string ReaderId, string ReaderSecret, string DeniedSecret, CommitReceipt Original)
{
    internal ProtectedDocumentRf3SetupPeer Peer { get; init; } = null!;
    internal KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveControlRecord Control { get; init; } = null!;
    internal string DeniedId { get; init; } = null!;
    internal const string Collection = "protected-documents";
    internal const string Entity = "one";
    internal const string Initial = "{\"title\":\"Київ original\",\"secret\":\"protected-private-canary\"}";

    internal static async Task<ProtectedDocumentRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, CancellationToken token)
    {
        var partition = new PartitionRef("protected-tenant", "protected-database", "protected-domain",
            "protected-" + Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        { FieldPolicies = [new("/secret", "private")] };
        _ = await McpCallerAssertions.SdkSuccessAsync(await source.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var reader = await PersistAsync(source, partition, Capability.DocumentsRead | Capability.Query, ["title"], token).ConfigureAwait(false);
        var denied = await PersistAsync(source, partition, Capability.None, [], token).ConfigureAwait(false);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, Entity, Initial, ExpectedRevision: 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(command, token));
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token).ConfigureAwait(false);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await source.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        var discovery = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, wave.Profile, token).ConfigureAwait(false);
        var peer = new ProtectedDocumentRf3SetupPeer(wave, directory, discovery.SiloAddress);
        var retired = await new ProtectedDocumentRf3Setup(peer, partition, placement).ExecuteAsync(token).ConfigureAwait(false);
        await Assert.That(retired.Control!.Phase).IsEqualTo(PartitionMovePhase.Retired);
        using var targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var target = new KeyLoadClient(targetHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var targetBefore = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await peer.VerifyRetainedOutcomesAsync(token).ConfigureAwait(false);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var targetAfter = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsGreaterThanOrEqualTo(sourceBefore.Applied);
        await Assert.That(targetAfter.Applied).IsGreaterThanOrEqualTo(targetBefore.Applied);
        return new(partition, new(partition, Collection, Entity), reader.Id, reader.Secret, denied.Secret, receipt) { Peer = peer, Control = retired.Control!, DeniedId = denied.Id };
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
        _ = await McpCallerAssertions.SdkSuccessAsync(await source.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(key, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))), token));
        return (id, secret);
    }
}
