using System.Net;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3CaptureAuthorityTrial
{
    private const string KeyPrefix = "restore-capture-";
    private const string Separator = ".";
    private const int SecretBytes = 32;

    internal static async Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var id = KeyPrefix + Guid.NewGuid().ToString(ClusterRestoreRf3Protocol.ArchiveFormat);
        var secret = id + Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(id, PartitionMovementPublicParentRf3Administrator.PrincipalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await ConfigureAsync(source, credential, cancellationToken).ConfigureAwait(false);
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1, secret,
            async (sdk, official) =>
            {
                var owner = seed.Directory.ControlOwner;
                var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(cancellationToken).ConfigureAwait(false));
                var request = new ClusterBackupOwnerRequest(ClusterRestoreRf3Protocol.CurrentVersion, Guid.NewGuid(), owner,
                    Guid.ParseExact(status.NodeId, ClusterRestoreRf3Protocol.IdentityFormat));
                var original = await McpCallerAssertions.SdkSuccessAsync(await sdk.CaptureClusterBackupOwnerAsync(request,
                    cancellationToken).ConfigureAwait(false));
                await ConfigureAsync(source, credential with { Revoked = true }, cancellationToken).ConfigureAwait(false);
                await DeniedAsync(sdk, official, seed.Partition, request, cancellationToken).ConfigureAwait(false);
                await ConfigureAsync(source, credential, cancellationToken).ConfigureAwait(false);
                await ClusterRestoreRf3EventingReadOracle.FourAsync(original,
                    await McpCallerAssertions.SdkSuccessAsync(await sdk.CaptureClusterBackupOwnerAsync(request,
                        cancellationToken).ConfigureAwait(false)), sdk, official, seed.Partition,
                    ClusterRestoreRf3Protocol.CaptureTool, request, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConfigureAsync(TwoRf3MembershipWave source, ApiKeyRecord credential,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(source.Application, TwoRf3MembershipProtocol.Node1);
        var root = new KeyLoadClient(http, source.Profile.AdminKey, IntegrationClientOptions.Execution());
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), credential,
            cancellationToken).ConfigureAwait(false))).IsTrue();
    }

    private static async Task DeniedAsync(KeyLoadClient sdk, McpOfficialClient official, PartitionRef partition,
        ClusterBackupOwnerRequest request, CancellationToken cancellationToken)
    {
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.CaptureClusterBackupOwnerAsync(request,
            cancellationToken).ConfigureAwait(false), ErrorCode.Unauthenticated);
        var direct = await Assert.ThrowsAsync<HttpRequestException>(() => official.CallAsync(
            ClusterRestoreRf3Protocol.CaptureTool, request, cancellationToken));
        await Assert.That(direct).IsNotNull();
        await Assert.That(direct!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var sql = SqlRf3Protocol.Call(partition, ClusterRestoreRf3Protocol.CaptureTool, request);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ExecuteSqlAsync(sql,
            cancellationToken).ConfigureAwait(false), ErrorCode.Unauthenticated);
        var q1 = await Assert.ThrowsAsync<HttpRequestException>(() => official.CallAsync(SqlOperationProtocol.ToolName,
            sql, cancellationToken));
        await Assert.That(q1).IsNotNull();
        await Assert.That(q1!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
