namespace KeyLoad.UnitTests.DatabaseComposition;

internal sealed class TypedQueueGraphPrimaryIdentityTests
{
    [Test]
    public async Task TypedRetainedPrimaryJsonRefusesAtomicCompositionThenRepairsOriginalIdAndColdContinuation()
    {
        foreach (var from in new[] { true, false })
        {
            foreach (var json in TypedQueueGraphPrimaryIdentityFlow.Faults)
            {
                var failures = new List<Exception>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var db = new TestDatabase();
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                        () => TypedQueueGraphPrimaryIdentityFlow.RunAsync(db, from, json), failures);
                }, failures);
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            }
        }
    }
}
