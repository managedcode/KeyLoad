using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Runs the original persisted page intent, actual native postings and actual canonical checkpoint ACK.</summary>
internal static class NativeTextMaintenancePhaseFlow
{
    private const int EmptyEffects = 0;

    internal static async Task<TextMaintenanceCapabilityResult> FinishAsync(
        TestDatabase fixture, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var begin = await runtime.PhaseAsync(fixture, request,
            TextMaintenanceCapabilityKind.Begin, token: cancellationToken);
        return await FinishBeganAsync(fixture, runtime, request, begin, cancellationToken);
    }

    internal static async Task<TextMaintenanceCapabilityResult> FinishBeganAsync(TestDatabase fixture,
        NativeTextMaintenanceTestRuntime runtime, TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityResult begin, CancellationToken cancellationToken)
    {
        var database = fixture.Database;
        var page = database.ReadProjectionBatch(NativeTextMaintenanceTestValues.Principal,
            new(request.Consumer, ThroughSequence: begin.ReplayUpperSequence));
        var checkpointId = Guid.NewGuid();
        var original = new CommitProjectionBatchRequest(checkpointId, request.Consumer, page.Token, []);
        var prepared = await runtime.PhaseAsync(fixture, request,
            TextMaintenanceCapabilityKind.PreparePage, page, original, token: cancellationToken);
        await Assert.That(prepared.OriginalCheckpointIntent!.CommandId).IsEqualTo(checkpointId);
        _ = await runtime.PhaseAsync(fixture, request,
            TextMaintenanceCapabilityKind.ApplyIntent, token: cancellationToken);
        var actual = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionBatchResult>(fixture,
            OperationKind.CommitProjectionBatch, original, checkpointId, cancellationToken);
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(checkpointId);
        await Assert.That(actual.Receipt.Mutations.Length).IsEqualTo(EmptyEffects);
        _ = await runtime.PhaseAsync(fixture, request,
            TextMaintenanceCapabilityKind.SettleCheckpoint, acknowledged: actual, token: cancellationToken);
        var complete = await runtime.PhaseAsync(fixture, request,
            TextMaintenanceCapabilityKind.Verify, token: cancellationToken);
        await Assert.That(complete.Checkpoint).IsEqualTo(page.ThroughSequence);
        await Assert.That(complete.ThroughSequence).IsEqualTo(page.ThroughSequence);
        return complete;
    }
}
