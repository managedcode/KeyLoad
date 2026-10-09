using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Actual concurrently started native public calls, all settled and compared as complete original receipts.</summary>
internal static class ClusterRestoreRf3ConcurrentCapture
{
    private const int FirstResult = 0;

    internal static async Task<ClusterBackupOwnerReceipt> RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionRef sqlPartition, ClusterBackupOwnerRequest request, CancellationToken cancellationToken)
    {
        var sql = SqlRf3Protocol.Call(sqlPartition, ClusterRestoreRf3Protocol.CaptureTool, request);
        var children = Task.WhenAll(SdkAsync(sdk, request, cancellationToken),
            McpAsync(official, request, cancellationToken),
            SqlRf3Protocol.SdkAsync<ClusterBackupOwnerReceipt>(sdk, sql, cancellationToken),
            SqlRf3Protocol.McpAsync<ClusterBackupOwnerReceipt>(official, sql, cancellationToken));
        ClusterBackupOwnerReceipt[] actual;
        try
        { actual = await children.ConfigureAwait(false); }
        catch (Exception failure)
        { throw children.Exception ?? new AggregateException(failure); }
        var original = actual[FirstResult];
        foreach (var receipt in actual)
        { await SqlRf3Protocol.EqualAsync(original, receipt); }
        return original;
    }

    private static async Task<ClusterBackupOwnerReceipt> SdkAsync(KeyLoadClient sdk,
        ClusterBackupOwnerRequest request, CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.CaptureClusterBackupOwnerAsync(request,
            cancellationToken).ConfigureAwait(false));

    private static async Task<ClusterBackupOwnerReceipt> McpAsync(McpOfficialClient official,
        ClusterBackupOwnerRequest request, CancellationToken cancellationToken)
        => (await McpCallerAssertions.SuccessAsync<ClusterBackupOwnerReceipt>(await official.CallAsync(
            ClusterRestoreRf3Protocol.CaptureTool, request, cancellationToken).ConfigureAwait(false))).Value;
}
