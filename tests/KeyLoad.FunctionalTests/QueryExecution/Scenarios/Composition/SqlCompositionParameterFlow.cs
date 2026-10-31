using KeyLoad.FunctionalTests.DatabaseComposition;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Server;
using C = KeyLoad.FunctionalTests.DatabaseComposition.QueueGraphIdentityState;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlCompositionParameterFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        string? root = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SqlCompositionParameterFixture();
            root = fixture.Source.Directory;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    await BodyAsync(fixture, token);
                }
                catch (Exception)
                {
                    fixture.RetainEvidence();
                    throw;
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(root).IsNotNull();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task BodyAsync(SqlCompositionParameterFixture fixture, CancellationToken token)
    {
        fixture.Source.Configure(C.Collection, ResourceKind.Collection);
        fixture.Source.Configure(C.Graph, ResourceKind.Graph);
        fixture.Source.Configure(C.Queue, ResourceKind.WorkQueue);
        fixture.Source.Configure(SqlCompositionParameterData.DerivedQueue, ResourceKind.WorkQueue);
        fixture.Seed = await QueueGraphIdentityFlow.SeedAsync(fixture.Source, true);
        var forward = SqlCompositionParameterData.Forward(fixture, Guid.NewGuid());
        await SqlCompositionParameterRefusals.RunAsync(fixture, forward, false, token);
        await ExecuteAsync(fixture, forward, false, token);
        var reverse = SqlCompositionParameterData.Reverse(fixture, Guid.NewGuid());
        await SqlCompositionParameterRefusals.RunAsync(fixture, reverse, true, token);
        await ExecuteAsync(fixture, reverse, true, token);
        await WaveAsync(fixture, token);
        for (var cold = 0; cold < SqlCompositionParameterData.ColdCycles; cold++)
        {
            token.ThrowIfCancellationRequested();
            var image = QueueWholeFlowStorage.Bytes(fixture.Store);
            var position = fixture.Store.Position;
            var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
            fixture.Reopen();
            await SqlCompositionParameterAssertions.ConserveAsync(fixture, image, position, identity);
            await SqlCompositionParameterRefusals.RunAsync(fixture, forward, false, token);
            await SqlCompositionParameterRefusals.RunAsync(fixture, reverse, true, token);
            await WaveAsync(fixture, token);
            await SqlCompositionParameterAssertions.ConserveAsync(fixture, image, position, identity);
        }
    }

    private static async Task ExecuteAsync(SqlCompositionParameterFixture fixture, CommandRequest command,
        bool reverse, CancellationToken token)
    {
        var request = SqlCompositionParameterData.Request(command, reverse);
        var compiled = SqlOperationTestData.Compile(request, cancellationToken: token);
        await Assert.That(compiled.CommandKind).IsEqualTo(OperationKind.Batch);
        await Assert.That(compiled.ReadKind).IsNull();
        await Assert.That(compiled.CommandId).IsEqualTo(command.CommandId);
        var decoded = ((CommandRequest)compiled.Payload);
        await SqlCompositionParameterAssertions.EqualAsync(decoded, command);
        var operation = fixture.Database.CreateCommand(OperationKind.Batch, command.CommandId, C.Root,
            fixture.Seed.SeedAt.AddSeconds(reverse ? SqlCompositionParameterData.ReverseOperationOffsetSeconds
                : SqlCompositionParameterData.ForwardOperationOffsetSeconds), decoded);
        var expected = new CommitReceipt(command.CommandId, ExpectedCanonicalCommit.Next(fixture.Database, command.Partition), [SqlCompositionParameterData.Receipt(fixture, reverse)], DurabilityProfile.ProcessDurable, 1);
        var actual = fixture.Database.Apply(operation).Get<CommitReceipt>();
        await SqlCompositionParameterAssertions.RuntimeReceiptAsync(actual, expected);
        fixture.Originals.Add(new(command, operation, expected, SqlCompositionParameterData.Effect(fixture, reverse)));
        await SqlCompositionParameterAssertions.ModelsAsync(fixture);
        await SqlCompositionParameterAssertions.OutboxAsync(fixture);
    }

    private static async Task WaveAsync(SqlCompositionParameterFixture fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var originals = new[] { (fixture.Seed.Seed, fixture.Seed.SeedReceipt) }
            .Concat(fixture.Originals.Select(value => (value.Operation, value.Expected)));
        foreach (var (operation, expected) in originals)
        {
            var image = QueueWholeFlowStorage.Bytes(fixture.Store);
            var position = fixture.Store.Position;
            var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlCompositionParameterAssertions.RuntimeReceiptAsync(fixture.Database.Apply(operation).Get<CommitReceipt>(), expected);
                await SqlCompositionParameterAssertions.RuntimeReceiptAsync(fixture.Database.ResolveOutcome(operation).Get<CommitReceipt>(), expected);
                await SqlCompositionParameterAssertions.ModelsAsync(fixture);
                await SqlCompositionParameterAssertions.OutboxAsync(fixture);
            }, failures);
            await ServerFailureObserver.ObserveAsync(
                () => SqlCompositionParameterAssertions.ConserveAsync(fixture, image, position, identity), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
