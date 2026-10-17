using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.DocumentStorage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ClientApi.SdkResponseAdmissionProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkResponseAdmissionFlow
{
    internal static async Task RunAsync(bool contentLength)
    {
        var failures = new List<Exception>();
        TestDatabase? database = null;
        KeyLoadClientKestrelServer? server = null;
        SdkResponseAdmissionOwner? owner = null;
        var startupUncertain = false;
        await ServerFailureObserver.ObserveAsync(RunOwnedAsync, failures);
        await CleanupAsync(database, server, owner, startupUncertain, failures);
        ServerFailureObserver.ThrowIfAny(failures);

        async Task RunOwnedAsync()
        {
            database = new TestDatabase(timeProvider: new ControlledReadClock(DateTimeOffset.UnixEpoch));
            database.Configure(Collection, ResourceKind.Collection);
            await SdkResponseAdmissionAssertions.EmptyAsync(database);
            owner = new SdkResponseAdmissionOwner(database, contentLength);
            startupUncertain = true;
            server = await KeyLoadClientKestrelServer.StartAsync(owner.HandleAsync);
            startupUncertain = false;
            await ExerciseAsync(database, server, owner);
        }
    }

    private static async Task CleanupAsync(TestDatabase? database, KeyLoadClientKestrelServer? server,
        SdkResponseAdmissionOwner? owner, bool startupUncertain, List<Exception> failures)
    {
        var transportUncertain = startupUncertain;
        if (server is not null)
        {
            transportUncertain = true;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await server.DisposeAsync();
                transportUncertain = false;
            }, failures);
        }
        if (owner is not null)
        {
            foreach (var original in owner.Requests)
            {
                await ServerFailureObserver.ObserveAsync(() => original, failures);
            }
        }
        if (database is not null && !transportUncertain)
        {
            var closed = false;
            ServerFailureObserver.Observe(() =>
            {
                database.JoinOwnedResources();
                closed = true;
            }, failures);
            if (closed)
            {
                ServerFailureObserver.Observe(() => Directory.Delete(database.Directory, recursive: true), failures);
            }
        }
    }

    private static async Task ExerciseAsync(TestDatabase database, KeyLoadClientKestrelServer server,
        SdkResponseAdmissionOwner owner)
    {
        var bounded = RetainedClient(server);
        var refused = await bounded.CommitAsync(owner.Command);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await SdkResponseAdmissionAssertions.ProblemAsync(refused.Problem!, ErrorCode.UnknownWriteOutcome, WriteUnavailable);
        var original = owner.Original!;
        await SdkResponseAdmissionAssertions.FirstAsync(database, owner);
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        await CurrentAsync(database);
        var exactBytes = JsonSerializer.SerializeToUtf8Bytes(original, JsonDefaults.Options).Length;
        var oneUnder = Client(server, exactBytes - BelowMinimumJson);
        var excess = await oneUnder.CommitAsync(owner.Command);
        await Assert.That(excess.IsFailed).IsTrue();
        await Assert.That(excess.Value).IsNull();
        await SdkResponseAdmissionAssertions.ProblemAsync(excess.Problem!, ErrorCode.UnknownWriteOutcome, WriteUnavailable);
        await UnchangedAsync(database, outbox);
        var exact = Client(server, exactBytes);
        var replay = await exact.CommitAsync(owner.Command);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(JsonDefaults.Serialize(replay.Value).SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await UnchangedAsync(database, outbox);
        var reference = new EntityRef(database.Partition, Collection, Document);
        var deniedRead = await bounded.GetAsync(reference);
        await Assert.That(deniedRead.IsFailed).IsTrue();
        await Assert.That(deniedRead.Value).IsNull();
        await SdkResponseAdmissionAssertions.ProblemAsync(deniedRead.Problem!, ErrorCode.OwnershipLost, ReadUnavailable);
        await UnchangedAsync(database, outbox);
        var healthy = new KeyLoadClient(server.Client, Credential, UnitClientOptions.Execution());
        var read = await healthy.GetAsync(reference);
        await Assert.That(read.IsSuccess).IsTrue();
        var expected = new DocumentReadResult(new(reference, CommittedRevision, SdkResponseAdmissionProtocol.Json, false, []));
        await Assert.That(JsonDefaults.Serialize(read.Value).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await UnchangedAsync(database, outbox);
    }

    private static KeyLoadClient RetainedClient(KeyLoadClientKestrelServer server)
    {
        var original = new KeyLoadClientExecutionOptions { MaximumResponseBodyBytes = BelowMinimumJson };
        var client = new KeyLoadClient(server.Client, Credential, UnitClientOptions.Execution(original));
        original.MaximumResponseBodyBytes = new KeyLoadClientExecutionOptions().MaximumResponseBodyBytes;
        return client;
    }

    private static KeyLoadClient Client(KeyLoadClientKestrelServer server, int bytes)
        => new(server.Client, Credential, UnitClientOptions.Execution(new()
        {
            MaximumResponseBodyBytes = bytes
        }));

    private static async Task CurrentAsync(TestDatabase database)
    {
        var reference = new EntityRef(database.Partition, Collection, Document);
        var expected = new DocumentResult(reference, CommittedRevision, SdkResponseAdmissionProtocol.Json, false, []);
        await Assert.That(JsonDefaults.Serialize(database.Database.GetDocument(Root, reference))
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task UnchangedAsync(TestDatabase database, OutboxSnapshot original)
    {
        await CurrentAsync(database);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(original,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
    }
}
