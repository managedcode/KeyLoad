using KeyLoad.Server;

namespace KeyLoad.UnitTests.Authorization;

internal static class FieldWriteOutputFlow
{
    internal static async Task RunAsync(bool ancestor, CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? owner = null;
        var lifetime = new FieldWriteOutputLifetime();
        ServerFailureObserver.Observe(() => owner = new(), failures);
        if (owner is not null)
        {
            var state = new FieldWriteOutputState(owner);
            await ServerFailureObserver.ObserveAsync(() => FieldWriteOutputWarm.RunAsync(state, ancestor, token), failures);
            if (state.HealthyReceipt is not null && state.ReaderDenied is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => lifetime.ColdAsync(state, token), failures);
            }
            lifetime.Cleanup(owner, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
