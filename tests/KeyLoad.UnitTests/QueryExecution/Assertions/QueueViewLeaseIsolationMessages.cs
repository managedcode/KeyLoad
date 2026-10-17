using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationMessages
{
    internal static Task RequireAsync(QueueViewLeaseIsolationState state)
        => QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            await SameAsync(state.Owner.Database.InspectMessage(Root, state.Lane, First),
                new(new(First, state.Acked ? MessageState.Acked : MessageState.Leased, One,
                    state.Version, Initial, NotBefore, null, state.Acked ? null : Root, Initial,
                    state.Acked ? null : state.CurrentDeadline),
                    state.Acked ? null : FirstPayload, state.Acked ? null : FirstHeaders));
            await SameAsync(state.Owner.Database.InspectMessage(Root, state.Lane, Second),
                Ready(Second, SecondSequence, SecondPayload, SecondHeaders));
            if (state.Healthy)
            {
                await SameAsync(state.Owner.Database.InspectMessage(Root, state.Lane, Healthy),
                    Ready(Healthy, HealthySequence, HealthyPayload, HealthyHeaders));
            }
        });

    private static MessageInspection Ready(string id, long sequence, string payload, string headers)
        => new(new(id, MessageState.Ready, Empty, Initial, sequence, NotBefore, null), payload, headers);

    private static async Task SameAsync(MessageInspection? actual, MessageInspection expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
