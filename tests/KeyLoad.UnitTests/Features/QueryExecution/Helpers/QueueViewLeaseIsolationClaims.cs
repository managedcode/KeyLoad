using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationClaims
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: One, LeaseSeconds: LeaseSeconds);
        var expectedToken = QueueViewLeaseIsolationOperations.NextToken(state);
        var claims = new DeliveryClaims(state.Lane, First, Root, Initial, Initial, state.Owner.Store.Identity.Incarnation);
        state.Claim = await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.Receive, request,
            request.RequestId, operation => new ReceiveResult(request.RequestId,
                [new(First, FirstPayload, FirstHeaders, state.Owner.Database.Sign(claims), Initial,
                    operation.EvaluatedAt.AddSeconds(LeaseSeconds), One, Initial)], expectedToken));
        await Assert.That(JsonDefaults.Serialize(state.Owner.Database.Verify<DeliveryClaims>(state.Delivery.Token))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(claims))).IsTrue();
        state.CurrentDeadline = state.Delivery.LeaseUntil;
        await QueueViewLeaseIsolationMessages.RequireAsync(state);
    }

    internal static Task ActiveReplayAsync(QueueViewLeaseIsolationState state)
        => QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            var original = state.Claim ?? throw new InvalidOperationException(Absent);
            await QueueViewLeaseIsolationResults.SameAsync(state.Owner.Submit(original.Operation), original.Result);
            await QueueViewLeaseIsolationResults.SameAsync(state.Owner.Database.ResolveOutcome(original.Operation), original.Result);
        });
}
