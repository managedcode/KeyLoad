using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnWaitWholeFlow
{
    internal const string Root = "root";
    internal const long FirstPosition = 1;
    internal const int CorpusCount = 3;
    private const string Json = "{}";
    private const long Term = 1;
    private const int FirstRecord = 0;
    private const long Revision = 1;

    internal static async Task RunAsync(Func<ReplicaAppliedPositionWaitFixture,
        NativeAnnMaintenanceTestRuntime, AnnMaintenanceRequest, CommitReceipt, Task> operation,
        DatabaseLimits? limits = null, TimeProvider? clock = null)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ReplicaAppliedPositionWaitFixture(limits, clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = fixture.Canonical;
                database.Configure(AnnSeedTestSupport.Collection, ResourceKind.Collection);
                var receipt = await SeedAsync(fixture, TestContext.Current!.Execution.CancellationToken);
                var pin = NativeAnnMaintenanceTestData.Pin(database);
                await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var began = await runtime.PhaseAsync(database, pin, AnnMaintenanceCapabilityKind.Begin);
                    _ = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, pin, began.Source!.ThroughSequence);
                    await operation(fixture, runtime, pin, receipt);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<CommitReceipt> SeedAsync(ReplicaAppliedPositionWaitFixture fixture, CancellationToken token)
    {
        var mutations = new List<Mutation>();
        for (var index = FirstRecord; index < CorpusCount; index++)
        {
            var id = AnnSeedTestSupport.Id(index);
            mutations.Add(new PutDocument(AnnSeedTestSupport.Collection, id, Json));
            mutations.Add(new PutVector(AnnSeedTestSupport.Collection, id, AnnSeedTestSupport.Field,
                [index, 1, 0], AnnSeedTestSupport.Space(), Revision));
        }
        return CommitAsync(fixture, [.. mutations], token);
    }

    internal static async Task<CommitReceipt> CommitAsync(ReplicaAppliedPositionWaitFixture fixture,
        System.Collections.Immutable.ImmutableArray<Mutation> mutations, CancellationToken token)
    {
        var database = fixture.Canonical;
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition, mutations);
        var operation = database.Database.NormalizeOperation(new ReplicatedOperation(id, OperationKind.Batch,
            Root, database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)));
        if (fixture.Log.State.Term < Term)
        { fixture.Log.SaveTermAndVote(Term, fixture.Configuration.LocalId); }
        var index = checked(fixture.Log.State.LastIndex + FirstPosition);
        fixture.Log.Append([new ReplicaEntry(index, Term, operation)]);
        fixture.Materializer.Commit(index);
        await fixture.Materializer.WaitForApplyAsync(index, token);
        var receipt = database.Database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Position).IsEqualTo(index);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(NativeSerialization.Serialize(database.Database.ResolveOutcome(operation).Get<CommitReceipt>())
            .SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        return receipt;
    }

    internal static WaitForAnnIndexRequest Request(TestDatabase database, AnnMaintenanceRequest pin, CommitReceipt receipt)
        => new(database.Partition, AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), pin.Consumer, pin.IndexGeneration, receipt.Token);

    internal static async Task HealthyAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        WaitForAnnIndexRequest request, CancellationToken token)
    {
        var result = await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(Root, request, token);
        await Assert.That(NativeSerialization.Serialize(result.IndexedToken)
            .SequenceEqual(NativeSerialization.Serialize(request.MinimumToken))).IsTrue();
        await Assert.That(result.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(result.SchemaVersion).IsEqualTo(FirstPosition);
        await Assert.That(result.PolicyEpoch).IsEqualTo(FirstPosition);
    }
}
