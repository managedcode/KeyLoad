using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class AtomicPartitionScopeLifecycleTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new TestDatabase(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, token), failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ExecuteAsync(TestDatabase fixture, CancellationToken token)
    {
        AtomicPartitionScopeLifecycleCommands.Configure(fixture);
        var state = new AtomicPartitionScopeLifecycleState(fixture.Partition, fixture.Store.Identity with
        {
            SigningKey = fixture.Store.Identity.SigningKey.ToArray()
        }, fixture.ExpectedReference);
        await Assert.That(state.Left.AtomicPartitionId).IsNotEqualTo(state.Right.AtomicPartitionId);
        await Assert.That(state.Left.AtomicPartitionId).IsNotEqualTo(state.Foreign.AtomicPartitionId);
        await Assert.That(state.Left.AtomicPartitionId).IsEqualTo("bef935b7fc791a12ebf7fac2b28fd245f4c496e6b616ae02b8ac8c8a6c5cbba6");
        await Assert.That(state.Right.AtomicPartitionId).IsEqualTo("36f92c96bc8e5eccfdd8d2474a3793662c342df27e6ef5da901997944064d045");
        await Assert.That(state.Foreign.AtomicPartitionId).IsEqualTo("8d4694897ce40067d130398716ab3c313c19c62fb5adc567c1fac4219006f2bf");
        await AtomicPartitionScopeLifecycleModels.CurrentAsync(fixture.Database, state, null, token);
        await AtomicPartitionScopeLifecycleReplay.RefuseAsync(fixture.Database, state,
            AtomicPartitionScopeLifecycleCommands.Domain(state.Left), AtomicPartitionScopeLifecycleCommands.DomainDetail, token);
        var shared = Guid.NewGuid();
        await AtomicPartitionScopeLifecycleReplay.WriteAsync(fixture.Database, state, shared, 0, false, token);
        await AtomicPartitionScopeLifecycleReplay.WriteAsync(fixture.Database, state, shared, 1, false, token);
        await AtomicPartitionScopeLifecycleReplay.RefuseAsync(fixture.Database, state,
            AtomicPartitionScopeLifecycleCommands.Unique(state.Left), AtomicPartitionScopeLifecycleCommands.UniqueDetail, token);
        await AtomicPartitionScopeLifecycleReplay.AllAsync(fixture.Database, state, token);
        fixture.JoinOwnedResources();
        await AtomicPartitionScopeLifecycleCold.RunAsync(fixture.Directory, state.OriginalIdentity,
            database => ColdHealthyAsync(database, state, token));
        await AtomicPartitionScopeLifecycleCold.RunAsync(fixture.Directory, state.OriginalIdentity,
            database => ColdOriginalsAsync(database, state, token));
    }
    private static async Task ColdHealthyAsync(DatabaseEngine database, AtomicPartitionScopeLifecycleState state, CancellationToken token)
    {
        await AtomicPartitionScopeLifecycleModels.CurrentAsync(database, state, null, token);
        await AtomicPartitionScopeLifecycleReplay.AllAsync(database, state, token);
        var healthy = Guid.NewGuid();
        await AtomicPartitionScopeLifecycleReplay.WriteAsync(database, state, healthy, 0, true, token);
        await AtomicPartitionScopeLifecycleReplay.WriteAsync(database, state, healthy, 1, true, token);
        await AtomicPartitionScopeLifecycleReplay.AllAsync(database, state, token);
    }
    private static async Task ColdOriginalsAsync(DatabaseEngine database, AtomicPartitionScopeLifecycleState state, CancellationToken token)
    {
        await AtomicPartitionScopeLifecycleModels.CurrentAsync(database, state, null, token);
        await AtomicPartitionScopeLifecycleReplay.AllAsync(database, state, token);
    }
}
