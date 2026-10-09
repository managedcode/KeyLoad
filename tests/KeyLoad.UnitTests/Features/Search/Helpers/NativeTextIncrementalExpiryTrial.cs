using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Owns only the original genuine page, actual signed expiry and immutable failed native ACK.</summary>
internal static class NativeTextIncrementalExpiryTrial
{
    private const string ExpiredDetail = "The projection batch token expired.";
    private const string IncompleteDetail = "The native text projection does not match the authorized source cut.";

    internal static async Task RunAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
        _ = await NativeTextMaintenanceSeed.CommitAsync(database, token);
        var request = await NativeTextMaintenanceRequestFixture.CreateAsync(database, token);
        var original = await PrepareAsync(database, request, token);
        var retained = await NativeTextIncrementalExpirySnapshot.CaptureAsync(database, request, token);
        var claims = database.Database.Verify<ProjectionBatchClaims>(original.Token);
        await Assert.That(claims.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(claims.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(claims.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        var delay = claims.ExpiresAt - database.Database.EvaluationClock.GetUtcNow();
        if (delay > TimeSpan.Zero)
        { await Task.Delay(delay, database.Database.EvaluationClock, token); }
        await Assert.That(database.Database.EvaluationClock.GetUtcNow()).IsGreaterThanOrEqualTo(claims.ExpiresAt);
        await retained.RequireAsync(database, token);
        await NativeTextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            await Assert.That(NativeSerialization.Serialize(begin.OriginalCheckpointIntent!).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
            _ = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.ApplyIntent, token: token);
            await RejectCheckpointAsync(database, runtime, request, original, token);
            await retained.RequireAsync(database, token);
        });
        await NativeTextIncrementalExpiryHealthy.RunAsync(database, request, token);
    }

    private static async Task<CommitProjectionBatchRequest> PrepareAsync(TestDatabase database,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        CommitProjectionBatchRequest? original = null;
        await NativeTextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            var page = database.Database.ReadProjectionBatch(NativeTextMaintenanceTestValues.Principal,
                new(request.Consumer, ThroughSequence: begin.ReplayUpperSequence));
            original = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
            var prepared = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.PreparePage, page, original, token: token);
            await Assert.That(NativeSerialization.Serialize(prepared.OriginalCheckpointIntent!).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        });
        return original ?? throw new InvalidOperationException();
    }

    private static async Task RejectCheckpointAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, CommitProjectionBatchRequest original, CancellationToken token)
    {
        var before = JsonDefaults.Serialize(database.Database.GetOutboxStatus(NativeTextMaintenanceTestValues.Principal, database.Partition));
        var failure = database.Submit(OperationKind.CommitProjectionBatch, original, id: original.CommandId);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(failure.Json).IsNull();
        await Assert.That(failure.NativeValue).IsNull();
        await Assert.That(failure.SafeDetail).IsEqualTo(ExpiredDetail);
        await Assert.That(JsonDefaults.Serialize(database.Database.GetOutboxStatus(NativeTextMaintenanceTestValues.Principal, database.Partition)).SequenceEqual(before)).IsTrue();
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var replay = database.Submit(OperationKind.CommitProjectionBatch, original, id: original.CommandId);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(failure))).IsTrue();
        TextMaintenanceCapabilityResult? partial = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
            await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Verify, token: token)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(error.Message).IsEqualTo(IncompleteDetail);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
