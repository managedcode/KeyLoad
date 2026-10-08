using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed record RemoteDocumentRf3Seed(PrincipalRecord Principal, string Secret,
    CommandRequest Command, CommitReceipt Receipt, PhysicalOwnerDirectoryV1 Directory)
{
    private const string ProbePrincipalPrefix = "c1-probe-remote-";
    internal static async Task<RemoteDocumentRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, KeyLoadClient destination, bool probe, CancellationToken token)
    {
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application,
            wave.Profile, token).ConfigureAwait(false);
        await ConfigureAsync(source, token).ConfigureAwait(false);
        await ConfigureAsync(destination, token).ConfigureAwait(false);
        var id = (probe ? ProbePrincipalPrefix : RemoteDocumentRf3Protocol.ReaderPrefix) + Guid.NewGuid().ToString(RemoteDocumentRf3Protocol.GuidFormat);
        var principal = new PrincipalRecord(id, RemoteDocumentRf3Protocol.Tenant,
            [new(RemoteDocumentRf3Protocol.Database, RemoteDocumentRf3Protocol.Collection, Capability.DocumentsRead)],
            [RemoteDocumentRf3Protocol.Title]);
        var keyId = RemoteDocumentRf3Protocol.KeyPrefix + Guid.NewGuid().ToString(RemoteDocumentRf3Protocol.GuidFormat);
        var secret = keyId + RemoteDocumentRf3Protocol.SecretSeparator
            + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RemoteDocumentRf3Protocol.SecretBytes));
        var credential = new ApiKeyRecord(keyId, id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await PersistAsync(source, principal, credential, token).ConfigureAwait(false);
        await PersistAsync(destination, principal with { Grants = [] }, credential, token).ConfigureAwait(false);
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, RemoteDocumentRf3Protocol.Partition,
            [new PutDocument(RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document,
                RemoteDocumentRf3Protocol.OriginalJson, ExpectedRevision: RemoteDocumentRf3Protocol.UnboundRevision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await destination.CommitAsync(command, token));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await source.BindAtomicPartitionPlacementAsync(
            Guid.NewGuid(), new(RemoteDocumentRf3Protocol.PlacementVersion, RemoteDocumentRf3Protocol.UnboundRevision,
                RemoteDocumentRf3Protocol.Partition, directory.Owners.Single(entry =>
                    entry.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner.PhysicalShardId), token))).IsTrue();
        return new(principal, secret, command, receipt, directory);
    }

    private static async Task ConfigureAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var resource = new ResourceDefinition(RemoteDocumentRf3Protocol.Collection, ResourceKind.Collection,
            RemoteDocumentRf3Protocol.Domain)
        { FieldPolicies = [new(RemoteDocumentRf3Protocol.Secret, RemoteDocumentRf3Protocol.Classification)] };
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(RemoteDocumentRf3Protocol.Tenant, RemoteDocumentRf3Protocol.Database, resource), token));
    }

    private static async Task PersistAsync(KeyLoadClient sdk, PrincipalRecord principal,
        ApiKeyRecord credential, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(principal))).IsTrue();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token))).IsTrue();
    }

    internal async Task GrantDestinationAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var expected = Principal with { PolicyEpoch = RemoteDocumentRf3Protocol.GrantedPolicyEpoch };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }
}
