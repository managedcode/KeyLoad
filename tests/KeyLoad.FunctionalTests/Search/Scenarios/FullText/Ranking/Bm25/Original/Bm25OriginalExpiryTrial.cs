using KeyLoad.Core.Search;

namespace KeyLoad.FunctionalTests.Search;

internal static class Bm25OriginalExpiryTrial
{
    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var admitted = flow.Capture(null);
        var unadmitted = flow.Capture(null);
        var success = flow.Publish(admitted);
        await flow.RequireHealthyAsync(admitted, success);
        var admittedEntry = OrleansSerialization.Serialize(owner.Entry(admitted.CommandId));
        var firstPhase = OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((admitted).Value!.Input.Span);
        var secondPhase = OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((unadmitted).Value!.Input.Span);
        var clock = owner.Database.EvaluationClock;
        var expiry = firstPhase.OriginalExpiry > secondPhase.OriginalExpiry
            ? firstPhase.OriginalExpiry : secondPhase.OriginalExpiry;
        var beforeWait = Bm25OriginalPhaseRecoveryState.Read(owner);
        while (clock.GetUtcNow() < expiry)
        {
            var remaining = expiry - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)),
                    clock, Bm25OriginalPhaseRecoveryFlow.Token);
            }
        }
        await Assert.That(clock.GetUtcNow() >= expiry).IsTrue();
        await beforeWait.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, admitted, success, admittedEntry, expiry);
        var expired = flow.Reissue(unadmitted);
        await Assert.That(new DateTimeOffset(expired.Timestamp) >= expiry).IsTrue();
        await Bm25OriginalExpiryAssertions.PhaseAsync(unadmitted, expired);
        var family = Bm25OriginalExpiryAssertions.Family(flow);
        var beforeFailure = Bm25OriginalPhaseRecoveryState.Read(owner);
        var failed = flow.Publish(expired);
        await Bm25OriginalExpiryAssertions.FailureAsync(flow, expired, failed, beforeFailure, family);
        var failedEntry = OrleansSerialization.Serialize(owner.Entry(expired.CommandId));
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, unadmitted, failed, failedEntry, expiry);
        var beforeCold = Bm25OriginalPhaseRecoveryState.Read(owner);
        owner.Reopen();
        await beforeCold.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, admitted, success, admittedEntry, expiry);
        await Bm25OriginalExpiryAssertions.ReplayAsync(flow, unadmitted, failed, failedEntry, expiry);
        var healthy = flow.Capture(admitted.CommandId);
        await flow.RequireHealthyAsync(healthy, flow.Publish(healthy));
    }
}
