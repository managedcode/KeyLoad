using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Real public same-ID receipt replay, retaining independent owner cuts rather than equating them.</summary>
internal static class ClusterRestoreRf3Capture
{
    internal static async Task<ImmutableArray<ClusterBackupOwnerReceipt>> CaptureAsync(TwoRf3MembershipWave wave,
        PartitionRef sqlPartition, Guid captureId, CancellationToken cancellationToken)
    {
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile,
            cancellationToken).ConfigureAwait(false);
        var receipts = ImmutableArray.CreateBuilder<ClusterBackupOwnerReceipt>(directory.Owners.Length);
        foreach (var registered in directory.Owners)
        {
            var node = registered.Owner.PhysicalShardId == wave.Profile.PhysicalShardId
                ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
            receipts.Add(await CaptureOneAsync(wave, sqlPartition, captureId, registered.Owner, node,
                cancellationToken).ConfigureAwait(false));
        }
        return receipts.MoveToImmutable();
    }

    internal static Task<ClusterBackupOwnerReceipt> CaptureOneAsync(TwoRf3MembershipWave wave,
        PartitionRef sqlPartition, Guid captureId, PhysicalShardRecord owner, string node,
        CancellationToken cancellationToken)
        => ClusterRestoreRf3CallerOwner.RunAsync(wave.Application, node, wave.Profile.AdminKey,
            (sdk, official) => CaptureOwnerAsync(sdk, official, sqlPartition, captureId, owner, cancellationToken), cancellationToken);

    private static async Task<ClusterBackupOwnerReceipt> CaptureOwnerAsync(KeyLoadClient sdk,
        McpOfficialClient official, PartitionRef sqlPartition, Guid captureId, PhysicalShardRecord owner,
        CancellationToken cancellationToken)
    {
        var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(cancellationToken).ConfigureAwait(false));
        var request = new ClusterBackupOwnerRequest(ClusterRestoreRf3Protocol.CurrentVersion, captureId,
            owner, Guid.ParseExact(status.NodeId, ClusterRestoreRf3Protocol.IdentityFormat));
        var original = await ClusterRestoreRf3ConcurrentCapture.RequireAsync(sdk, official, sqlPartition,
            request, cancellationToken).ConfigureAwait(false);
        await RequireIdentityAsync(original, request).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await McpCallerAssertions.SdkSuccessAsync(await sdk
            .CaptureClusterBackupOwnerAsync(request, cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<ClusterBackupOwnerReceipt>(
            await official.CallAsync(ClusterRestoreRf3Protocol.CaptureTool, request, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(sqlPartition, ClusterRestoreRf3Protocol.CaptureTool, request);
        await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.SdkAsync<ClusterBackupOwnerReceipt>(sdk,
            sql, cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.McpAsync<ClusterBackupOwnerReceipt>(official,
            sql, cancellationToken).ConfigureAwait(false));
        return original;
    }

    private static async Task RequireIdentityAsync(ClusterBackupOwnerReceipt actual, ClusterBackupOwnerRequest request)
    {
        await Assert.That(actual.Version).IsEqualTo(ClusterRestoreRf3Protocol.CurrentVersion);
        await Assert.That(actual.ArchiveId).IsEqualTo(request.CaptureId.ToString(ClusterRestoreRf3Protocol.ArchiveFormat));
        await Assert.That(actual.Cut.CaptureId).IsEqualTo(request.CaptureId);
        await Assert.That(actual.Cut.SourceNodeId).IsEqualTo(request.ExpectedNodeId);
        await SqlRf3Protocol.EqualAsync(request.ExpectedOwner, actual.Cut.Owner);
        await SqlRf3Protocol.EqualAsync(request.ExpectedOwner, actual.Cut.Catalog.DefaultShard);
        await Assert.That(actual.Cut.StorePosition).IsGreaterThan(0L);
        await Assert.That(actual.Cut.AppliedIndex).IsGreaterThan(0L);
        await Assert.That(actual.ManifestDigest).IsNotEmpty();
        foreach (var partition in actual.Cut.Partitions)
        {
            await Assert.That(partition.CanonicalRecordCount).IsGreaterThanOrEqualTo(0L);
            await Assert.That(partition.CanonicalDigest).IsNotEmpty();
            await Assert.That(partition.RosterDigest).IsNotEmpty();
        }
    }
}
