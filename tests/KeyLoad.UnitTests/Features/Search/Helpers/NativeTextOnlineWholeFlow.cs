using KeyLoad.Orleans;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextOnlineWholeFlow
{
    internal static async Task<OnlineTextIndexMaintenanceResult> RunAsync(TestDatabase fixture,
        NativeTextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest request, CancellationToken token,
        Func<Task>? whileNativeSnapshotPinned = null)
    {
        var session = Guid.NewGuid();
        var expiry = runtime.FreshExpiry(fixture);
        var original = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.ResolveOriginal, expiry, token);
        if (original.OriginalResult is { } replay)
        { return replay; }
        _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.Capture, expiry, token);
        if (whileNativeSnapshotPinned is not null)
        { await whileNativeSnapshotPinned(); }
        var state = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.Seed, expiry, token);
        state = await ReplayAsync(fixture, runtime, session, request, state, expiry, token);
        _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.Validate, expiry, token);
        var prepared = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.IssuePublication, expiry, token);
        var operation = prepared.IssuedPublication ?? throw new InvalidOperationException("Actual native publication was not issued.");
        var originalWork = runtime.Owner.RequirePublicationWork(session, request, NativeTextMaintenanceTestValues.Principal);
        var actual = await originalWork.AdmitOriginal(() => Task.Run(() => fixture.SubmitIssued(operation), token));
        var result = actual.Get<OnlineTextIndexMaintenanceResult>();
        var committed = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.ReconcileCommitted, expiry, token);
        await Assert.That(NativeSerialization.Serialize(committed.OriginalResult).SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await runtime.Owner.AbortAsync(session);
        return result;
    }

    private static async Task<OnlineTextCapabilityResult> ReplayAsync(TestDatabase fixture,
        NativeTextOnlineTestRuntime runtime, Guid session, OnlineTextIndexMaintenanceRequest request,
        OnlineTextCapabilityResult state, DateTimeOffset expiry, CancellationToken token)
    {
        for (var pageNumber = 0; pageNumber < runtime.Options.Core.TextMaintenance.Value.MaximumReplayPages; pageNumber++)
        {
            var page = fixture.Database.ReadProjectionBatch(NativeTextMaintenanceTestValues.Principal,
                new(request.Consumer, ThroughSequence: state.CurrentCut!.ThroughSequence));
            var id = OnlineTextCheckpointIdentity.Create(request.CommandId, page.ThroughSequence);
            var intent = new CommitProjectionBatchRequest(id, request.Consumer, page.Token, []);
            _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.PreparePage, expiry, token, page, intent);
            _ = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.ApplyIntent, expiry, token);
            var work = runtime.Owner.TryRequireCheckpointWork(intent, NativeTextMaintenanceTestValues.Principal, expiry)
                ?? throw new InvalidOperationException("Actual original checkpoint registration is absent.");
            var applied = await work.AdmitOriginal(() => Task.Run(() => fixture.Submit(OperationKind.CommitProjectionBatch, intent, id: id), token));
            var receipt = applied.Get<ProjectionBatchResult>();
            await Assert.That(receipt.Receipt.CommandId).IsEqualTo(id);
            await Assert.That(receipt.Receipt.Mutations).IsEmpty();
            state = await runtime.PhaseAsync(fixture, session, request, OnlineTextCapabilityKind.SettleCheckpoint, expiry, token, receipt: receipt);
            if (!page.HasMore)
            { return state; }
        }
        throw new InvalidOperationException("Actual native replay did not settle within its original centrally configured page bound.");
    }
}
