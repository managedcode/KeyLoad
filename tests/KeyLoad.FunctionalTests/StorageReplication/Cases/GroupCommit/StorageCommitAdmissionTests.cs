using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>AC-REP-008: a warm local commit owner admits ready updates before it publishes their effects.</summary>
internal sealed class StorageCommitAdmissionTests
{
    private const string Collection = "grouped-local-updates";
    private const string First = "first";
    private const string Second = "second";
    private const string Created = "{\"version\":1}";
    private const string Updated = "{\"version\":2}";
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;

    [Test]
    public async Task AcRep008WarmedLocalOwnerAdmitsBothUpdatesBeforePublishingEither()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var warmId = Guid.NewGuid();
        var observation = new WarmActivation();
        await using var node = new RequestCqrsHostedRuntimeFixture(configureServices: (services, _) =>
            services.AddSingleton<IIncomingGrainCallFilter>(observation));
        await node.InitializeAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(node.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var database = node.Database;
        var incarnation = database.Store.Identity.Incarnation;
        var owner = node.Grains.GetGrain<IPartitionCommitGrain>(
            PartitionCommitGrainIdentity.For(incarnation, database.Partition));
        var creation = Guid.NewGuid();
        database.Configure(Collection, ResourceKind.Collection, id: creation);
        var seed = new CommandRequest(warmId, database.Partition,
            [new PutDocument(Collection, First, Created, 0), new PutDocument(Collection, Second, Created, 0)]);
        var seedCommand = database.Database.CreateCommand(OperationKind.Batch, warmId, RequestCqrsHostedProfile.Root,
            database.Database.EvaluationClock.GetUtcNow(), seed);
        var seeded = await owner.ApplyAsync<CommitReceipt>(incarnation, database.Partition, seedCommand, bounded);
        await Assert.That(seeded.IsSuccess).IsTrue();
        var activation = await observation.Context.WaitAsync(bounded);
        var before = database.ReadCanonicalCut();
        var first = Update(database, First);
        var second = Update(database, Second);

        var admitted = await AdmitInOneTurn(activation, database, first, second, bounded).WaitAsync(bounded);
        var replies = await Task.WhenAll(admitted.First, admitted.Second).WaitAsync(bounded);

        await Assert.That(before.AppliedPosition).IsEqualTo(seeded.Value!.Token.Position);
        await Assert.That(admitted.Between).IsEqualTo(before);
        await Assert.That(replies[0].IsSuccess).IsTrue();
        await Assert.That(replies[1].IsSuccess).IsTrue();
        await RequireUpdateAsync(database, creation, replies[0].Value!, first.CommandId, First, before.AppliedPosition + 1, bounded);
        await RequireUpdateAsync(database, creation, replies[1].Value!, second.CommandId, Second, before.AppliedPosition + 2, bounded);
        var after = database.ReadCanonicalCut();
        await Assert.That(after.AppliedPosition).IsEqualTo(before.AppliedPosition + 2);
        await Assert.That(after.StorePosition).IsGreaterThan(before.StorePosition);
        await RequireReplayAsync(database, owner, first, replies[0].Value!, bounded);
        await RequireReplayAsync(database, owner, second, replies[1].Value!, bounded);
        await Assert.That(database.ReadCanonicalCut()).IsEqualTo(after);
    }

    private static Command<DatabaseCommand> Update(RequestCqrsHostedDatabase database, string id)
    {
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, database.Partition,
            [new PutDocument(Collection, id, Updated, InitialRevision, ExplicitReplacement: true)]);
        return database.Database.CreateCommand(OperationKind.Batch, commandId, RequestCqrsHostedProfile.Root,
            database.Database.EvaluationClock.GetUtcNow(), request);
    }

    private static Task<AdmittedUpdates> AdmitInOneTurn(IGrainContext activation, RequestCqrsHostedDatabase database,
        Command<DatabaseCommand> first, Command<DatabaseCommand> second, CancellationToken token)
    {
        var turn = new Task<AdmittedUpdates>(() =>
        {
            var owner = activation.GrainInstance as PartitionCommitGrain
                ?? throw new InvalidOperationException("The warmed partition commit activation was lost.");
            var incarnation = database.Store.Identity.Incarnation;
            var initial = owner.ApplyAsync<CommitReceipt>(incarnation, database.Partition, first, token);
            var between = database.ReadCanonicalCut();
            var following = owner.ApplyAsync<CommitReceipt>(incarnation, database.Partition, second, token);
            return new(initial, between, following);
        }, token);
        activation.Scheduler.QueueTask(turn);
        return turn;
    }

    private static async Task RequireUpdateAsync(RequestCqrsHostedDatabase database, Guid creation,
        CommitReceipt receipt, Guid commandId, string id, long position, CancellationToken token)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(position);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(1);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        var mutation = receipt.Mutations.Single();
        await Assert.That(mutation.Kind).IsEqualTo("putDocument");
        await Assert.That(mutation.Resource).IsEqualTo(Collection);
        await Assert.That(mutation.Id).IsEqualTo(id);
        await Assert.That(mutation.Revision).IsEqualTo(UpdatedRevision);
        var document = database.Database.GetDocument(RequestCqrsHostedProfile.Root,
            database.Reference(creation, Collection, id), cancellationToken: token);
        await Assert.That(document!.Json).IsEqualTo(Updated);
        await Assert.That(document.Revision).IsEqualTo(UpdatedRevision);
    }

    private static async Task RequireReplayAsync(RequestCqrsHostedDatabase database, IPartitionCommitGrain owner,
        Command<DatabaseCommand> operation, CommitReceipt receipt, CancellationToken token)
    {
        var replayed = await owner.ApplyAsync<CommitReceipt>(database.Store.Identity.Incarnation, database.Partition,
            operation, token);
        await Assert.That(replayed.IsSuccess).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(replayed.Value!).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
    }

    private sealed record AdmittedUpdates(Task<Result<CommitReceipt>> First, (long AppliedPosition, long StorePosition) Between,
        Task<Result<CommitReceipt>> Second);

    private sealed class WarmActivation : IIncomingGrainCallFilter
    {
        private readonly TaskCompletionSource<IGrainContext> context = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<IGrainContext> Context => context.Task;

        public Task Invoke(IIncomingGrainCallContext call)
        {
            if (call.Grain is PartitionCommitGrain)
            {
                context.TrySetResult(call.TargetContext);
            }
            return call.Invoke();
        }
    }
}
