using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterBackupPrincipalCaptureReceipts
{
    internal static async Task RequireIdentityAsync(ClusterBackupOwnerReceipt actual, ClusterBackupOwnerRequest request)
    {
        await Assert.That(actual.Version).IsEqualTo(ClusterRestoreRf3Protocol.CurrentVersion);
        await Assert.That(actual.ArchiveId).IsEqualTo(request.CaptureId.ToString(ClusterRestoreRf3Protocol.ArchiveFormat));
        await Assert.That(actual.Cut.Version).IsEqualTo(ClusterRestoreRf3Protocol.CurrentVersion);
        await Assert.That(actual.Cut.CaptureId).IsEqualTo(request.CaptureId);
        await Assert.That(actual.Cut.SourceNodeId).IsEqualTo(request.ExpectedNodeId);
        await SqlRf3Protocol.EqualAsync(request.ExpectedOwner, actual.Cut.Owner);
        await SqlRf3Protocol.EqualAsync(request.ExpectedOwner, actual.Cut.Catalog.DefaultShard);
        await Assert.That(actual.Cut.StorePosition).IsGreaterThan(ClusterBackupPrincipalCaptureProtocol.EmptyPosition);
        await Assert.That(actual.Cut.AppliedIndex).IsGreaterThan(ClusterBackupPrincipalCaptureProtocol.EmptyPosition);
        await Assert.That(actual.ManifestDigest).IsNotEmpty();
        await Assert.That(actual.Cut.CanonicalDigest).IsNotEmpty();
        foreach (var partition in actual.Cut.Partitions)
        {
            await Assert.That(partition.CanonicalRecordCount).IsGreaterThanOrEqualTo(ClusterBackupPrincipalCaptureProtocol.EmptyPosition);
            await Assert.That(partition.CanonicalDigest).IsNotEmpty();
            await Assert.That(partition.RosterDigest).IsNotEmpty();
        }
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient official, PartitionRef partition,
        ClusterBackupPrincipalCaptureState state, Func<Task> current, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Original, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CaptureClusterBackupOwnerAsync(state.Request, token).ConfigureAwait(false)));
        await current().ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(state.Original, (await McpCallerAssertions.SuccessAsync<ClusterBackupOwnerReceipt>(
            await official.CallAsync(ClusterRestoreRf3Protocol.CaptureTool, state.Request, token).ConfigureAwait(false))).Value);
        await current().ConfigureAwait(false);
        var sql = SqlRf3Protocol.Call(partition, ClusterRestoreRf3Protocol.CaptureTool, state.Request);
        await SqlRf3Protocol.EqualAsync(state.Original,
            await SqlRf3Protocol.SdkAsync<ClusterBackupOwnerReceipt>(sdk, sql, token).ConfigureAwait(false));
        await current().ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(state.Original,
            await SqlRf3Protocol.McpAsync<ClusterBackupOwnerReceipt>(official, sql, token).ConfigureAwait(false));
        await current().ConfigureAwait(false);
    }
}
