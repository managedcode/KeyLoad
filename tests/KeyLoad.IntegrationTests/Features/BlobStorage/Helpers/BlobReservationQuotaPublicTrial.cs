using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobReservationQuotaPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                var reserved = await BlobReservationQuotaPublicPublication.ReserveAsync(callers, token);
                await BlobReservationQuotaPublicAssertions.ReservedAsync(callers, reserved, reserved.Begun.Value, token);
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    await BlobReservationQuotaPublicAssertions.RefusedAsync(callers, reserved.Refused, token);
                    await BlobReservationQuotaPublicAssertions.ReservedAsync(callers, reserved, reserved.Begun.Value, token);
                }
                var abort = new AbortBlobUploadRequest(Guid.NewGuid(), reserved.First.Blob, reserved.First.UploadId);
                var aborted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                    await callers.Mcp.CallAsync("keyload_blobs_abort_upload", abort, token))).Value;
                var expected = reserved.Begun.Value with
                {
                    Status = BlobUploadStatus.Aborted
                };
                await SqlRf3Protocol.EqualAsync(expected, aborted.Value);
                await BlobReservationQuotaPublicAssertions.RefusedAsync(callers, reserved.Refused, token);
                await BlobReservationQuotaPublicAssertions.ReservedAsync(callers, reserved, expected, token);
                var healthy = await BlobReservationQuotaPublicPublication.PublishAsync(callers, reserved.Refused, token);
                await BlobReservationQuotaPublicAssertions.HealthyAsync(callers, healthy, token);
                await BlobReservationQuotaPublicAssertions.RefusedAsync(callers, reserved.Refused, token);
                await BlobReservationQuotaPublicAssertions.HealthyAsync(callers, healthy, token);
                await BlobReservationQuotaPublicAssertions.OriginalAsync(callers, reserved, abort, aborted, token);
                await BlobSupersessionPublicAssertions.ReplayAsync(callers, healthy, token);
                await BlobReservationQuotaPublicAssertions.HealthyAsync(callers, healthy, token);
                return true;
            }, token);
    }
}
