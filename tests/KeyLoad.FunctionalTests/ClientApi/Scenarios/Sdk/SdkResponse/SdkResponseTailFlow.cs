using KeyLoad.Client;
using KeyLoad.FunctionalTests.DocumentStorage;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;
using static KeyLoad.FunctionalTests.ClientApi.SdkResponseAdmissionProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkResponseTailFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? database = null;
        KeyLoadClientKestrelServer? server = null;
        SdkResponseTailOwner? owner = null;
        var startupUncertain = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            database = new(timeProvider: new ControlledReadClock(DateTimeOffset.UnixEpoch));
            database.Configure(Collection, ResourceKind.Collection);
            await SdkResponseAdmissionAssertions.EmptyAsync(database);
            owner = new(database);
            startupUncertain = true;
            server = await KeyLoadClientKestrelServer.StartAsync(owner.HandleAsync);
            startupUncertain = false;
            await ExerciseAsync(database, server, owner, token);
        }, failures);
        owner?.Release();
        await CleanupAsync(database, server, owner, startupUncertain, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(TestDatabase database, KeyLoadClientKestrelServer server,
        SdkResponseTailOwner owner, CancellationToken token)
    {
        var expected = owner.Original.Expected;
        var budget = JsonDefaults.Serialize(expected).Length;
        var bounded = new KeyLoadClient(server.Client, Credential,
            UnitClientOptions.Execution(new()
            {
                MaximumResponseBodyBytes = budget
            }));
        var original = owner.SendAsync(bounded, token);
        await owner.Withholding.WaitAsync(token);
        var refused = await original.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        await Assert.That(owner.WasReleased).IsFalse();
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await SdkResponseAdmissionAssertions.ProblemAsync(refused.Problem!, ErrorCode.UnknownWriteOutcome, WriteUnavailable);
        await SdkResponseAdmissionAssertions.FirstAsync(database, owner.Original);
        await Assert.That(JsonDefaults.Serialize(owner.Original.Original).Length).IsEqualTo(budget);
        var outcome = DocumentCommandOutcomeTestSupport.OutcomeBytes(database, Root, expected.CommandId);
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        var position = database.Store.Position;
        owner.Release();
        foreach (var producer in owner.Producers)
        {
            await producer.WaitAsync(token);
        }
        var healthy = new KeyLoadClient(server.Client, Credential, UnitClientOptions.Execution());
        var replay = await healthy.CommitAsync(owner.Original.Command, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(JsonDefaults.Serialize(replay.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var reference = new EntityRef(database.Partition, Collection, Document);
        var read = await healthy.GetAsync(reference, token);
        await Assert.That(read.IsSuccess).IsTrue();
        var document = new DocumentReadResult(new(database.ExpectedReference(reference), CommittedRevision, SdkResponseAdmissionProtocol.Json, false, []));
        await Assert.That(JsonDefaults.Serialize(read.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(document))).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutcomeBytes(database, Root, expected.CommandId).AsSpan()
            .SequenceEqual(outcome)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(outbox,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(owner.RequestCount).IsEqualTo(SdkResponseTailProtocol.ExpectedRequests);
    }

    private static async Task CleanupAsync(TestDatabase? database, KeyLoadClientKestrelServer? server,
        SdkResponseTailOwner? owner, bool startupUncertain, List<Exception> failures)
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
            if (owner.SdkRequest is { } originalSdk)
            {
                await ServerFailureObserver.ObserveAsync(() => originalSdk, failures);
            }
            foreach (var original in owner.Producers)
            {
                await ServerFailureObserver.ObserveAsync(() => original, failures);
            }
        }
        if (database is not null && !transportUncertain)
        {
            if (failures.Count == 0)
            {
                ServerFailureObserver.Observe(database.Dispose, failures);
            }
            else
            {
                ServerFailureObserver.Observe(database.JoinOwnedResources, failures);
            }
        }
    }
}
