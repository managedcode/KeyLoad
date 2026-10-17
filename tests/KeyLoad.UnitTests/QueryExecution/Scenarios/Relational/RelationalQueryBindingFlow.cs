using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using static KeyLoad.UnitTests.Features.QueryExecution.RelationalQueryBindingFixture;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class RelationalQueryBindingFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase fixture, CancellationToken token)
    {
        Provision(fixture);
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        await RelationalQueryBindingAssertions.InvalidAsync(engine, fixture.Store, fixture.Partition, token);
        var seed = Seed(fixture);
        var expectedToken = fixture.Store.Read(view => DatabaseEngine.Token(view, fixture.Partition, fixture.Store.Position));
        await BinderFieldLineagePages.EqualAsync(new CommitReceipt(seed.Command.CommandId, expectedToken,
            [new("putDocument", Collection, Id, Revision)], DurabilityProfile.ProcessDurable), seed.Receipt);
        await RelationalQueryBindingAssertions.RecordAsync(fixture.Database, fixture.Partition, token);
        await RelationalQueryBindingAssertions.InvalidAsync(engine, fixture.Store, fixture.Partition, token);
        await RelationalQueryBindingAssertions.HealthyAsync(engine, fixture.Store, fixture.Partition, token);
        await PolicyAsync(fixture, engine, token);
        await BinderFieldLineageAuthority.ReplayAsync(fixture.Database, fixture.Store, seed.Command, seed.Receipt, token);
        await RelationalQueryBindingCold.RunAsync(fixture, seed.Command, seed.Receipt, token);
    }

    private static async Task PolicyAsync(TestDatabase fixture, QueryEngine engine, CancellationToken token)
    {
        Policy(fixture, DeniedEpoch, false);
        await RelationalQueryBindingAssertions.DeniedAsync(engine, fixture.Store, fixture.Partition, token);
        Policy(fixture, RepairedEpoch, true);
        await RelationalQueryBindingAssertions.RecordAsync(fixture.Database, fixture.Partition, token);
        await RelationalQueryBindingAssertions.InvalidAsync(engine, fixture.Store, fixture.Partition, token);
        await RelationalQueryBindingAssertions.HealthyAsync(engine, fixture.Store, fixture.Partition, token);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(Reader,
            new(fixture.Partition, Projection, AllowFullScan: true), cancellationToken: cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        await CapabilityOutputOperations.ConserveAsync(fixture.Store, image, position, identity);
        await RelationalQueryBindingAssertions.HealthyAsync(engine, fixture.Store, fixture.Partition, token);
    }
}
