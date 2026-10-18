using System.Globalization;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropBudgetFailure
{
    internal static async Task ExecuteAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await SeedRowsAsync(profile, token).ConfigureAwait(false);
        var before = await NativeDropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var id = Guid.NewGuid();
        var failed = await NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, token).Original.ConfigureAwait(false);
        await Assert.That(failed.Error).IsEqualTo((ErrorCode?)ErrorCode.BudgetExceeded);
        await Assert.That(failed.Payload.IsEmpty).IsTrue();
        await NativeDropRuntimeAssertions.FailureImageAsync(profile, id, before, ErrorCode.BudgetExceeded, token).ConfigureAwait(false);
        var original = await NativeDropRuntimeAssertions.StoredDropAsync(profile, id,
            NativeDropRuntimeProtocol.Request, ErrorCode.BudgetExceeded, token).ConfigureAwait(false);
        await NativeDropOperationFlow.RejectedReplayAsync(profile, id, failed, original, token).ConfigureAwait(false);
        await NativeDropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        for (var cold = NativeDropRuntimeProtocol.FirstCold; cold < NativeDropRuntimeProtocol.ColdReopens; cold++)
        {
            await profile.RestartAsync(token).ConfigureAwait(false);
            await NativeDropOperationFlow.RejectedReplayAsync(profile, id, failed, original, token).ConfigureAwait(false);
            await NativeDropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        }
    }

    private static async Task SeedRowsAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        for (var index = NativeDropRuntimeProtocol.FirstCold; index <= NativeDropRuntimeProtocol.TinyScanRecords; index++)
        {
            var row = "bounded-row-" + index.ToString(CultureInfo.InvariantCulture);
            var json = JsonSerializer.Serialize(new { id = row, value = "bounded" }, JsonDefaults.Options);
            var id = Guid.NewGuid();
            var request = new CommandRequest(id, NativeDropRuntimeProtocol.Partition,
                [new PutDocument(NativeDropRuntimeProtocol.Table, row, json)]);
            var reply = await NativeDropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, id,
                PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(request), token).ConfigureAwait(false);
            await Assert.That(NativeDropRuntimeAssertions.Value<CommitReceipt>(reply).CommandId).IsEqualTo(id);
        }
    }
}
