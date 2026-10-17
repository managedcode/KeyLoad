using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterBackupPrincipalCaptureSeed
{
    internal static async Task<(PrincipalRecord Principal, string Credential)> CreateAsync(
        TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var principal = new PrincipalRecord(ClusterBackupPrincipalCaptureProtocol.PrincipalPrefix
            + Guid.NewGuid().ToString(ClusterRestoreRf3Protocol.ArchiveFormat), seed.Partition.TenantId, [], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = ClusterBackupPrincipalCaptureProtocol.InitialEpoch
        };
        var key = ClusterBackupPrincipalCaptureProtocol.KeyPrefix + Guid.NewGuid().ToString(ClusterRestoreRf3Protocol.ArchiveFormat);
        var credential = key + ClusterBackupPrincipalCaptureProtocol.Separator
            + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(ClusterBackupPrincipalCaptureProtocol.SecretBytes));
        await ConfigureAsync(source, principal, token).ConfigureAwait(false);
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            source.Profile.AdminKey, async (sdk, _) =>
            {
                var definition = new ApiKeyRecord(key, principal.Id,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential))));
                await Assert.That((await McpCallerAssertions.SdkStatusAsync(await sdk.ConfigureApiKeyAsync(
                    Guid.NewGuid(), definition, token).ConfigureAwait(false))).IsSuccess).IsTrue();
                return true;
            }, token).ConfigureAwait(false);
        return (principal, credential);
    }

    internal static async Task ConfigureAsync(TwoRf3MembershipWave source, PrincipalRecord principal,
        CancellationToken token)
    {
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            source.Profile.AdminKey, async (sdk, _) =>
            {
                await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                    await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token).ConfigureAwait(false)));
                return true;
            }, token).ConfigureAwait(false);
    }
}
