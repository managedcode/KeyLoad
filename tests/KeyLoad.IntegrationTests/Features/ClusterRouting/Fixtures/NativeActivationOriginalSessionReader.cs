using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one genuine persisted reader and its actual SDK/official MCP callers across the original migration.</summary>
internal static class NativeActivationOriginalSessionReader
{
    private const string ReaderPrefix = "migration-reader-";
    private const string GuidFormat = "N";
    private const string CredentialKeySuffix = "-key";
    private const string CredentialSeparator = ".";
    private const int SecretBytes = 32;
    private const string PutDocumentKind = "putDocument";
    internal static async Task RunAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        CommandRequest command, CommitReceipt receipt, Func<NativeActivationOriginalSessionState, Task> operation,
        CancellationToken token)
    {
        var placement = await NativeActivationMigrationRf3Ownership.ReadPlacementAsync(administrator,
            command.Partition, token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var reference = new EntityRef(command.Partition, RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId);
        var expected = new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Partition.AtomicPartitionId,
                receipt.Token.Position, placement.PlacementEpoch),
            [new(PutDocumentKind, reference.Collection, reference.Id, NativeActivationRf3Protocol.CommittedRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await NativeActivationOriginalSessionConservation.RequireReceiptAsync(expected, receipt);
        var principalId = ReaderPrefix + Guid.NewGuid().ToString(GuidFormat);
        var declared = new PrincipalRecord(principalId, command.Partition.TenantId,
            [new(command.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection, Capability.DocumentsRead)], [])
        {
            PolicyEpoch = 1L,
            ClusterAdministrator = false
        };
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), declared, token));
        await SqlRf3Protocol.EqualAsync(declared, stored);
        var keyId = principalId + CredentialKeySuffix;
        var secret = keyId + CredentialSeparator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var key = new ApiKeyRecord(keyId, stored.Id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await McpCallerAssertions.SdkStatusAsync(await administrator.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), key, token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var readers = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
                TwoRf3MembershipProtocol.Node1, secret, token);
            await ServerFailureObserver.ObserveAsync(() => operation(new(command, receipt,
                expected, stored, readers)), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
