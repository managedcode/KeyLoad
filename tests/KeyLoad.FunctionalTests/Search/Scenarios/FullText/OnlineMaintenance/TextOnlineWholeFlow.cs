using KeyLoad.Clustering;
using KeyLoad.Clustering.Search;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextOnlineWholeFlow
{
    internal static async Task<OnlineTextIndexMaintenanceResult> RunAsync(TestDatabase fixture,
        TextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest request, CancellationToken token,
        Func<Task>? whileSnapshotPinned = null)
    {
        var session = Guid.NewGuid();
        var expiry = TextOnlineTestRuntime.FreshExpiry(fixture);
        var original = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.ResolveOriginal, expiry, token);
        if (original.OriginalResult is { } replay)
        {
            return replay;
        }
        _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.Capture, expiry, token);
        if (whileSnapshotPinned is not null)
        {
            await whileSnapshotPinned();
        }
        var state = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.Seed, expiry, token);
        state = await ReplayAsync(fixture, runtime, session, request, state, expiry, token);
        _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.Validate, expiry, token);
        var prepared = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.IssuePublication, expiry, token);
        var operation = prepared.IssuedPublication ?? throw new InvalidOperationException("Actual execution publication was not issued.");
        var originalWork = runtime.Owner.RequirePublicationWork(session, request, TextMaintenanceTestValues.Principal);
        var actual = await originalWork.AdmitOriginal(() => Task.Run(() => fixture.SubmitIssued(operation), token));
        var result = actual.Get<OnlineTextIndexMaintenanceResult>();
        var committed = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.ReconcileCommitted, expiry, token);
        await Assert.That(OrleansSerialization.Serialize(committed.OriginalResult).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
        await runtime.Owner.AbortAsync(session);
        return result;
    }

    private static async Task<OnlineTextCapabilityResult> ReplayAsync(TestDatabase fixture,
        TextOnlineTestRuntime runtime, Guid session, OnlineTextIndexMaintenanceRequest request,
        OnlineTextCapabilityResult state, DateTimeOffset expiry, CancellationToken token)
    {
        while (true)
        {
            var page = fixture.Database.ReadProjectionBatch(TextMaintenanceTestValues.Principal,
                new(request.Consumer, ThroughSequence: state.CurrentCut!.ThroughSequence));
            var id = OnlineTextCheckpointIdentity.Create(request.CommandId, page.ThroughSequence);
            var intent = new CommitProjectionBatchRequest(id, request.Consumer, page.Token, []);
            _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.PreparePage, expiry, token, page, intent);
            _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.ApplyIntent, expiry, token);
            var work = runtime.Owner.TryRequireCheckpointWork(intent, TextMaintenanceTestValues.Principal, expiry)
                ?? throw new InvalidOperationException("Actual original checkpoint registration is absent.");
            var applied = await work.AdmitOriginal(() => Task.Run(() => fixture.Submit(OperationKind.CommitProjectionBatch, intent, id: id), token));
            var receipt = applied.Get<ProjectionBatchResult>();
            await Assert.That(receipt.Receipt.CommandId).IsEqualTo(id);
            await Assert.That(receipt.Receipt.Mutations).IsEmpty();
            state = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.SettleCheckpoint, expiry, token, receipt: receipt);
            if (!page.HasMore)
            {
                return state;
            }
        }
    }
}
