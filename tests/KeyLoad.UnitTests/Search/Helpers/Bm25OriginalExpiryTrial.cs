using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25OriginalExpiryTrial
{
    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var admitted = flow.Capture(null);
        var unadmitted = flow.Capture(null);
        var success = flow.Publish(admitted);
        await flow.RequireHealthyAsync(admitted, success);
        var admittedEntry = NativeSerialization.Serialize(owner.Entry(admitted.Id));
        var firstPhase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(admitted);
        var secondPhase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(unadmitted);
        var clock = owner.Database.EvaluationClock;
        var expiry = firstPhase.OriginalExpiry > secondPhase.OriginalExpiry
            ? firstPhase.OriginalExpiry : secondPhase.OriginalExpiry;
        var beforeWait = Bm25OriginalPhaseRecoveryState.Read(owner);
        var remaining = expiry - clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining + TimeSpan.FromTicks(1), clock, Bm25OriginalPhaseRecoveryFlow.Token);
        }
        await Assert.That(clock.GetUtcNow() >= expiry).IsTrue();
        await beforeWait.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, admitted, success, admittedEntry, expiry);
        var expired = flow.Reissue(unadmitted);
        await Assert.That(expired.EvaluatedAt >= expiry).IsTrue();
        await Bm25OriginalExpiryAssertions.PhaseAsync(unadmitted, expired);
        var family = Bm25OriginalExpiryAssertions.Family(flow);
        var beforeFailure = Bm25OriginalPhaseRecoveryState.Read(owner);
        var failed = flow.Publish(expired);
        await Bm25OriginalExpiryAssertions.FailureAsync(flow, expired, failed, beforeFailure, family);
        var failedEntry = NativeSerialization.Serialize(owner.Entry(expired.Id));
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, unadmitted, failed, failedEntry, expiry);
        var beforeCold = Bm25OriginalPhaseRecoveryState.Read(owner);
        owner.Reopen();
        await beforeCold.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, admitted, success, admittedEntry, expiry);
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, unadmitted, failed, failedEntry, expiry);
        var healthy = flow.Capture(admitted.Id);
        await flow.RequireHealthyAsync(healthy, flow.Publish(healthy));
    }
}
