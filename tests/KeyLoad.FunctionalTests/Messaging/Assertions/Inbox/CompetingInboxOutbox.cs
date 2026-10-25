namespace KeyLoad.FunctionalTests.Messaging;

internal static class CompetingInboxOutbox
{
    internal static async Task RequireAsync(DatabaseEngine database, CompetingInboxState state)
    {
        var first = new OutboxEntry(1, 0, state.Producer.Token, state.Time,
            new EnqueueMessage(CompetingInboxState.Queue, "input", "{\"n\":1}"),
            new("enqueue", CompetingInboxState.Queue, "input", 1));
        var effects = state.Effects(state.Winner);
        var expected = new List<OutboxEntry> { first };
        for (var index = 0; index < effects.Length; index++)
        {
            expected.Add(new(index + 2, index, state.Receipt.Token, state.Time, effects[index], state.Receipt.Mutations[index],
                After: index == 0 ? new DocumentRecord(state.DocumentReference,
                    1, state.Json(state.Winner), new(), state.Time) : null));
        }
        await SubscriptionOldCutOutbox.EntriesAsync(database, state.Partition, expected);
    }
}
