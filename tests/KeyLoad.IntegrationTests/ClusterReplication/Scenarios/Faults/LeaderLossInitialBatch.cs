using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class LeaderLossInitialBatch
{
    private const string Collection = "orders";
    private const string Events = "events";
    private const string Jobs = "jobs";
    private const string Activity = "activity";

    internal static async Task<LeaderLossInitialCommand> CommitAsync(KeyLoadClient[] clients, PartitionRef partition,
        Func<ManagedCode.Communication.Result<CommitReceipt>, CommitReceipt> success, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, "o1", "{\"number\":1}", 0),
            new AppendEvents(Events, "o1", [new("e1", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Jobs, "m1", "{}"),
            new PublishTopic(Activity, [new("t1", "Created", "{}"), new("t2", "Updated", "{}"), new("t3", "Updated", "{}")])
        ]);
        var committed = success(await clients[2].CommitAsync(command, cancellationToken));
        await Assert.That(committed.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        return new(command, committed);
    }
}

internal sealed record LeaderLossInitialCommand(CommandRequest Command, CommitReceipt Receipt);
