using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RevokedOutcomeAdmissionModel
{
    private const string ProjectionAdministrator = "root";
    private const string ProjectionAdministrationDenied = "System projection administration is required.";
    internal static async Task RequireAsync(RevokedOutcomeAdmissionFixture fixture, bool fresh, CancellationToken token)
    {
        await RevokedOutcomeAdmissionCursor.HealthyAsync(fixture, fresh, token);
        var stream = new StreamRef(fixture.Harness.Partition, RevokedOutcomeAdmissionFixture.Events, "stream");
        EventRecord[] events = [new(stream, 1, 1, new("original", "Created", "{\"event\":1}"), RevokedOutcomeAdmissionFixture.Epoch)];
        if (fresh)
        {
            events = [.. events, new(stream, 2, 2, new("healthy", "Created", "{\"event\":2}"), RevokedOutcomeAdmissionFixture.Epoch)];
        }
        await RevokedOutcomeAdmissionValues.EqualAsync(fixture.Database.ReadStream(RevokedOutcomeAdmissionFixture.Worker, new ReadStreamRequest(stream), token), new StreamPage(stream, new(events.Length, 1, 1), [.. events], fixture.Database.LastApplied,
                false, null, fixture.Database.LastApplied));
        var queue = new QueueLaneRef(fixture.Harness.Partition, RevokedOutcomeAdmissionFixture.Queue);
        await RevokedOutcomeAdmissionValues.EqualAsync(fixture.Database.InspectMessage(RevokedOutcomeAdmissionFixture.Worker, queue, "original"),
            new MessageInspection(new("original", MessageState.Ready, 0, 1, 1, null, null), "{\"work\":1}", "{}"));
        await RevokedOutcomeAdmissionValues.EqualAsync(fixture.Database.InspectMessage(RevokedOutcomeAdmissionFixture.Worker, queue, "healthy"),
            fresh ? new MessageInspection(new("healthy", MessageState.Ready, 0, 1, 2, null, null), "{\"work\":2}", "{}") : null);
        await OutboxAsync(fixture, fresh);
        await RevokedOutcomeAdmissionValues.OriginalAsync(fixture);
    }
    private static async Task OutboxAsync(RevokedOutcomeAdmissionFixture fixture, bool fresh)
    {
        var commands = fresh ? new[] { fixture.Original, fixture.Fresh! } : new[] { fixture.Original };
        var sequence = 0L;
        var bytes = 0L;
        for (var producer = 0; producer < commands.Length; producer++)
        {
            var command = commands[producer];
            var effects = RevokedOutcomeAdmissionValues.Effects(producer != 0);
            var commit = producer == 0 ? fixture.OriginalToken : fixture.FreshToken;
            for (var index = 0; index < command.Mutations.Length; index++)
            {
                sequence++;
                var mutation = command.Mutations[index];
                var after = mutation is PutDocument put ? new DocumentRecord(fixture.Harness.ExpectedReference(new(fixture.Harness.Partition,
                    RevokedOutcomeAdmissionFixture.Documents, put.Id)), 1, put.Json, new(), RevokedOutcomeAdmissionFixture.Epoch) : null;
                var expected = new OutboxEntry(sequence, index, commit, RevokedOutcomeAdmissionFixture.Epoch, mutation, effects[index], After: after);
                var raw = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox", fixture.Harness.Partition, sequence)))
                    ?? throw new InvalidOperationException("A literal producer outbox entry is missing.");
                bytes += raw.LongLength;
                var actual = OrleansSerialization.Deserialize<OutboxEntry>(raw);
                await RevokedOutcomeAdmissionValues.EqualAsync(actual, expected);
                await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
            }
        }
        var rows = fixture.Store.Read(view => view.Scan(KeySpace.Partition("outbox", fixture.Harness.Partition), 8));
        await Assert.That(rows.HasMore).IsFalse();
        await Assert.That(rows.Records.Length).IsEqualTo((int)sequence);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.GetOutboxStatus(
                RevokedOutcomeAdmissionFixture.Worker, fixture.Harness.Partition));
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(denied.Message).IsEqualTo(ProjectionAdministrationDenied);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionValues.EqualAsync(
            fixture.Database.GetOutboxStatus(ProjectionAdministrator, fixture.Harness.Partition),
            new OutboxStatus(new(sequence, 1, sequence, bytes), [])), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
