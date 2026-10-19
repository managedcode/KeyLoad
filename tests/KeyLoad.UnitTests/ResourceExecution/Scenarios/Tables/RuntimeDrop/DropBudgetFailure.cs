using System.Globalization;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class DropBudgetFailure
{
    internal static async Task ExecuteAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        await SeedRowsAsync(profile, token).ConfigureAwait(false);
        var before = await DropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var id = Guid.NewGuid();
        var failed = await DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, token).Original.ConfigureAwait(false);
        await Assert.That(failed.Error).IsEqualTo((ErrorCode?)ErrorCode.BudgetExceeded);
        await Assert.That(failed.Payload.IsEmpty).IsTrue();
        await DropRuntimeAssertions.FailureImageAsync(profile, id, before, ErrorCode.BudgetExceeded, token).ConfigureAwait(false);
        var original = await DropRuntimeAssertions.StoredDropAsync(profile, id,
            DropRuntimeProtocol.Request, ErrorCode.BudgetExceeded, token).ConfigureAwait(false);
        await DropOperationFlow.RejectedReplayAsync(profile, id, failed, original, token).ConfigureAwait(false);
        await DropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        for (var cold = DropRuntimeProtocol.FirstCold; cold < DropRuntimeProtocol.ColdReopens; cold++)
        {
            await profile.RestartAsync(token).ConfigureAwait(false);
            await DropOperationFlow.RejectedReplayAsync(profile, id, failed, original, token).ConfigureAwait(false);
            await DropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        }
    }

    private static async Task SeedRowsAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        for (var index = DropRuntimeProtocol.FirstCold; index <= DropRuntimeProtocol.TinyScanRecords; index++)
        {
            var row = "bounded-row-" + index.ToString(CultureInfo.InvariantCulture);
            var json = JsonSerializer.Serialize(new { id = row, value = "bounded" }, JsonDefaults.Options);
            var id = Guid.NewGuid();
            var request = new CommandRequest(id, DropRuntimeProtocol.Partition,
                [new PutDocument(DropRuntimeProtocol.Table, row, json)]);
            var reply = await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, id,
                PartitionStoreProtocol.AdministratorId, OrleansSerialization.Serialize(request), token).ConfigureAwait(false);
            await Assert.That(DropRuntimeAssertions.Value<CommitReceipt>(reply).CommandId).IsEqualTo(id);
        }
    }
}
