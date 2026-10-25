using KeyLoad.Server;
using P = KeyLoad.FunctionalTests.StorageRecovery.CheckpointOriginalCauseProtocol;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class CheckpointOriginalCauseTests
{
    [Test]
    [Arguments(P.Compact)]
    [Arguments(P.Install)]
    public async Task RealIdentityWriteFailurePreservesCauseAndPoisonUntilRecoveredOwnerCompletesCheckpoint(string mode)
    {
        CurrentStorageFixture? files = null;
        var lifetime = new StorageFailureOwnerLifetime();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            files = new();
            await CheckpointOriginalCauseFlow.RunAsync(files, mode, lifetime);
        }, failures);
        if (files is not null && lifetime.CanAdvance)
        {
            ServerFailureObserver.Observe(files.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
